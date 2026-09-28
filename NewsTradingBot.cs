using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(
        TimeZone = TimeZones.EAfricaStandardTime,
        AccessRights = AccessRights.None
    )]
    public class NewsTradingBot : Robot
    {
        private const string BuyLabel = "NewsTradingBot_BUY";
        private const string SellLabel = "NewsTradingBot_SELL";

        private Symbol _tradeSymbol;
        private DateTime _newsTime;
        private DateTime _placementTime;

        private bool _ordersPlaced;
        private bool _eventHandled;

        [Parameter("Symbol", DefaultValue = "XAUUSD")]
        public string TradeSymbol { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double LotSize { get; set; }

        [Parameter(
            "News Time",
            DefaultValue = "2026-09-28 19:00:00"
        )]
        public string NewsTime { get; set; }

        [Parameter("Seconds Before News", DefaultValue = 3, MinValue = 1)]
        public int SecondsBeforeNews { get; set; }

        [Parameter("Distance (Pips)", DefaultValue = 500, MinValue = 1)]
        public double DistancePips { get; set; }

        [Parameter("Stop Loss (USD)", DefaultValue = 10, MinValue = 0)]
        public double StopLossUsd { get; set; }

        [Parameter("Take Profit (USD)", DefaultValue = 20, MinValue = 0)]
        public double TakeProfitUsd { get; set; }

        [Parameter(
            "Pending Expiration (Seconds)",
            DefaultValue = 60,
            MinValue = 1
        )]
        public int PendingExpirationSeconds { get; set; }

        [Parameter(
            "Maximum Spread (Pips)",
            DefaultValue = 0,
            MinValue = 0
        )]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Debug", DefaultValue = true)]
        public bool Debug { get; set; }

        protected override void OnStart()
        {
            _tradeSymbol = Symbols.GetSymbol(TradeSymbol);

            if (_tradeSymbol == null)
            {
                Print("ERROR: Symbol '{0}' was not found.", TradeSymbol);
                Stop();
                return;
            }

            if (!DateTime.TryParse(NewsTime, out _newsTime))
            {
                Print(
                    "ERROR: Invalid News Time '{0}'. Use format: yyyy-MM-dd HH:mm:ss",
                    NewsTime
                );

                Stop();
                return;
            }

            _placementTime = _newsTime.AddSeconds(-SecondsBeforeNews);

            _ordersPlaced = false;
            _eventHandled = false;

            Timer.Start(TimeSpan.FromMilliseconds(100));

            Print("========================================");
            Print("News Trading Bot started");
            Print("Symbol: {0}", _tradeSymbol.Name);
            Print("News Time: {0}", _newsTime);
            Print(
                "Order Placement Time: {0}",
                _placementTime
            );
            Print(
                "Distance: {0} pips",
                DistancePips
            );
            Print(
                "Lot Size: {0}",
                LotSize
            );
            Print(
                "SL: ${0}",
                StopLossUsd
            );
            Print(
                "TP: ${0}",
                TakeProfitUsd
            );
            Print(
                "Pending Expiration: {0} seconds",
                PendingExpirationSeconds
            );
            Print("========================================");
        }

        protected override void OnTimer()
        {
            if (_eventHandled)
                return;

            DateTime now = Server.Time;

            // Place the pending orders at the requested time.
            if (!_ordersPlaced && now >= _placementTime)
            {
                PlaceNewsOrders();
                return;
            }

            // If neither order triggered before expiration,
            // cancel any remaining pending orders.
            if (_ordersPlaced &&
                now >= _newsTime.AddSeconds(PendingExpirationSeconds))
            {
                CancelAllPendingOrders();

                _eventHandled = true;

                if (Debug)
                    Print("Pending-order expiration reached.");
            }
        }

        protected override void OnTick()
        {
            if (!_ordersPlaced || _eventHandled)
                return;

            // If one pending order has triggered,
            // immediately cancel the remaining opposite order.
            var botPositions = Positions
                .Where(p =>
                    p.SymbolName == _tradeSymbol.Name &&
                    (p.Label == BuyLabel || p.Label == SellLabel))
                .ToArray();

            if (botPositions.Length > 0)
            {
                if (Debug)
                    Print(
                        "Bot position detected. Cancelling remaining pending orders."
                    );

                CancelAllPendingOrders();

                _eventHandled = true;
            }
        }

        private void PlaceNewsOrders()
        {
            if (_ordersPlaced || _eventHandled)
                return;

            if (!_tradeSymbol.MarketHours.IsOpened())
            {
                Print(
                    "Market is closed. News orders were not placed."
                );

                _eventHandled = true;
                return;
            }

            double spreadPips =
                (_tradeSymbol.Ask - _tradeSymbol.Bid) /
                _tradeSymbol.PipSize;

            if (MaximumSpreadPips > 0 &&
                spreadPips > MaximumSpreadPips)
            {
                Print(
                    "Spread too high: {0:F2} pips. Maximum allowed: {1:F2} pips.",
                    spreadPips,
                    MaximumSpreadPips
                );

                _eventHandled = true;
                return;
            }

            double volumeInUnits =
                _tradeSymbol.QuantityToVolumeInUnits(LotSize);

            volumeInUnits =
                _tradeSymbol.NormalizeVolumeInUnits(
                    volumeInUnits,
                    RoundingMode.Down
                );

            if (volumeInUnits < _tradeSymbol.VolumeInUnitsMin)
            {
                Print(
                    "ERROR: Requested lot size is below the symbol minimum volume."
                );

                _eventHandled = true;
                return;
            }

            if (volumeInUnits > _tradeSymbol.VolumeInUnitsMax)
            {
                Print(
                    "ERROR: Requested lot size is above the symbol maximum volume."
                );

                _eventHandled = true;
                return;
            }

            // BUY STOP = current Ask + 500 pips
            double buyStopPrice =
                _tradeSymbol.Ask +
                DistancePips * _tradeSymbol.PipSize;

            // SELL STOP = current Bid - 500 pips
            double sellStopPrice =
                _tradeSymbol.Bid -
                DistancePips * _tradeSymbol.PipSize;

            buyStopPrice = NormalizePrice(buyStopPrice);
            sellStopPrice = NormalizePrice(sellStopPrice);

            double stopLossPips =
                MoneyToPips(StopLossUsd, volumeInUnits);

            double takeProfitPips =
                MoneyToPips(TakeProfitUsd, volumeInUnits);

            DateTime expiration =
                Server.Time.AddSeconds(PendingExpirationSeconds);

            if (Debug)
            {
                Print("========================================");
                Print("PLACING NEWS ORDERS");
                Print("Current Bid: {0}", _tradeSymbol.Bid);
                Print("Current Ask: {0}", _tradeSymbol.Ask);
                Print("Spread: {0:F2} pips", spreadPips);
                Print("BUY STOP: {0}", buyStopPrice);
                Print("SELL STOP: {0}", sellStopPrice);
                Print("SL: {0:F2} pips", stopLossPips);
                Print("TP: {0:F2} pips", takeProfitPips);
                Print("Expiration: {0}", expiration);
                Print("========================================");
            }

            TradeResult buyResult = PlaceStopOrder(
                TradeType.Buy,
                _tradeSymbol.Name,
                volumeInUnits,
                buyStopPrice,
                BuyLabel,
                stopLossPips > 0 ? stopLossPips : (double?)null,
                takeProfitPips > 0 ? takeProfitPips : (double?)null,
                ProtectionType.Relative,
                expiration,
                "News Buy Stop",
                false,
                StopTriggerMethod.Trade
            );

            if (!buyResult.IsSuccessful)
            {
                Print(
                    "BUY STOP failed: {0}",
                    buyResult.Error
                );
            }
            else
            {
                if (Debug)
                    Print(
                        "BUY STOP placed successfully. ID: {0}",
                        buyResult.PendingOrder.Id
                    );
            }

            TradeResult sellResult = PlaceStopOrder(
                TradeType.Sell,
                _tradeSymbol.Name,
                volumeInUnits,
                sellStopPrice,
                SellLabel,
                stopLossPips > 0 ? stopLossPips : (double?)null,
                takeProfitPips > 0 ? takeProfitPips : (double?)null,
                ProtectionType.Relative,
                expiration,
                "News Sell Stop",
                false,
                StopTriggerMethod.Trade
            );

            if (!sellResult.IsSuccessful)
            {
                Print(
                    "SELL STOP failed: {0}",
                    sellResult.Error
                );
            }
            else
            {
                if (Debug)
                    Print(
                        "SELL STOP placed successfully. ID: {0}",
                        sellResult.PendingOrder.Id
                    );
            }

            _ordersPlaced = true;

            // If either order failed, cancel the other one.
            if (!buyResult.IsSuccessful || !sellResult.IsSuccessful)
            {
                Print(
                    "One of the two pending orders failed. Cancelling any remaining order."
                );

                CancelAllPendingOrders();

                _eventHandled = true;
            }
        }

        private double MoneyToPips(
            double money,
            double volumeInUnits)
        {
            if (money <= 0)
                return 0;

            if (_tradeSymbol.PipValue <= 0)
                return 0;

            double moneyPerPipForThisVolume =
                _tradeSymbol.PipValue *
                (volumeInUnits / _tradeSymbol.LotSize);

            if (moneyPerPipForThisVolume <= 0)
                return 0;

            return money / moneyPerPipForThisVolume;
        }

        private double NormalizePrice(double price)
        {
            if (_tradeSymbol.TickSize <= 0)
                return price;

            double ticks =
                Math.Round(
                    price / _tradeSymbol.TickSize,
                    MidpointRounding.AwayFromZero
                );

            return ticks * _tradeSymbol.TickSize;
        }

        private void CancelAllPendingOrders()
        {
            var pendingOrders = PendingOrders
                .Where(order =>
                    order.SymbolName == _tradeSymbol.Name &&
                    (order.Label == BuyLabel ||
                     order.Label == SellLabel))
                .ToArray();

            foreach (var order in pendingOrders)
            {
                TradeResult result =
                    CancelPendingOrder(order);

                if (Debug)
                {
                    if (result.IsSuccessful)
                    {
                        Print(
                            "Cancelled pending order ID {0}.",
                            order.Id
                        );
                    }
                    else
                    {
                        Print(
                            "Failed to cancel pending order ID {0}: {1}",
                            order.Id,
                            result.Error
                        );
                    }
                }
            }
        }

        protected override void OnStop()
        {
            CancelAllPendingOrders();

            Timer.Stop();

            if (Debug)
                Print("News Trading Bot stopped.");
        }
    }
}