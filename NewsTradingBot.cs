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
        private const string BuyLabel1 = "NewsTradingBot_BUY_500";
        private const string BuyLabel2 = "NewsTradingBot_BUY_1000";

        private const string SellLabel1 = "NewsTradingBot_SELL_500";
        private const string SellLabel2 = "NewsTradingBot_SELL_1000";

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

        [Parameter("Distance 1 (Pips)", DefaultValue = 500, MinValue = 1)]
        public double Distance1Pips { get; set; }

        [Parameter("Distance 2 (Pips)", DefaultValue = 1000, MinValue = 1)]
        public double Distance2Pips { get; set; }

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
                Print(
                    "ERROR: Symbol '{0}' was not found.",
                    TradeSymbol
                );

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

            _placementTime =
                _newsTime.AddSeconds(-SecondsBeforeNews);

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
                "Distance 1: {0} pips",
                Distance1Pips
            );
            Print(
                "Distance 2: {0} pips",
                Distance2Pips
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

            // Place the four pending orders at the requested time.
            if (!_ordersPlaced && now >= _placementTime)
            {
                PlaceNewsOrders();
                return;
            }

            // Cancel remaining pending orders after expiration.
            if (_ordersPlaced &&
                now >= _newsTime.AddSeconds(PendingExpirationSeconds))
            {
                CancelAllPendingOrders();

                _eventHandled = true;

                if (Debug)
                    Print(
                        "Pending-order expiration reached."
                    );
            }
        }

        protected override void OnTick()
        {
            if (!_ordersPlaced || _eventHandled)
                return;

            bool buyTriggered =
                Positions.Any(p =>
                    p.SymbolName == _tradeSymbol.Name &&
                    (p.Label == BuyLabel1 ||
                     p.Label == BuyLabel2));

            bool sellTriggered =
                Positions.Any(p =>
                    p.SymbolName == _tradeSymbol.Name &&
                    (p.Label == SellLabel1 ||
                     p.Label == SellLabel2));

            // If a BUY executes:
            // cancel BOTH SELL STOP orders.
            if (buyTriggered)
            {
                if (Debug)
                    Print(
                        "BUY position detected. Cancelling both SELL STOP orders."
                    );

                CancelSellPendingOrders();
            }

            // If a SELL executes:
            // cancel BOTH BUY STOP orders.
            if (sellTriggered)
            {
                if (Debug)
                    Print(
                        "SELL position detected. Cancelling both BUY STOP orders."
                    );

                CancelBuyPendingOrders();
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

            // -----------------------------------------
            // BUY STOP PRICES
            // -----------------------------------------

            double buyStop1Price =
                _tradeSymbol.Ask +
                Distance1Pips * _tradeSymbol.PipSize;

            double buyStop2Price =
                _tradeSymbol.Ask +
                Distance2Pips * _tradeSymbol.PipSize;

            // -----------------------------------------
            // SELL STOP PRICES
            // -----------------------------------------

            double sellStop1Price =
                _tradeSymbol.Bid -
                Distance1Pips * _tradeSymbol.PipSize;

            double sellStop2Price =
                _tradeSymbol.Bid -
                Distance2Pips * _tradeSymbol.PipSize;

            buyStop1Price = NormalizePrice(buyStop1Price);
            buyStop2Price = NormalizePrice(buyStop2Price);

            sellStop1Price = NormalizePrice(sellStop1Price);
            sellStop2Price = NormalizePrice(sellStop2Price);

            double stopLossPips =
                MoneyToPips(
                    StopLossUsd,
                    volumeInUnits
                );

            double takeProfitPips =
                MoneyToPips(
                    TakeProfitUsd,
                    volumeInUnits
                );

            DateTime expiration =
                Server.Time.AddSeconds(
                    PendingExpirationSeconds
                );

            if (Debug)
            {
                Print("========================================");
                Print("PLACING FOUR NEWS ORDERS");

                Print(
                    "Current Bid: {0}",
                    _tradeSymbol.Bid
                );

                Print(
                    "Current Ask: {0}",
                    _tradeSymbol.Ask
                );

                Print(
                    "Spread: {0:F2} pips",
                    spreadPips
                );

                Print(
                    "BUY STOP 1 (+{0} pips): {1}",
                    Distance1Pips,
                    buyStop1Price
                );

                Print(
                    "BUY STOP 2 (+{0} pips): {1}",
                    Distance2Pips,
                    buyStop2Price
                );

                Print(
                    "SELL STOP 1 (-{0} pips): {1}",
                    Distance1Pips,
                    sellStop1Price
                );

                Print(
                    "SELL STOP 2 (-{0} pips): {1}",
                    Distance2Pips,
                    sellStop2Price
                );

                Print(
                    "SL: {0:F2} pips",
                    stopLossPips
                );

                Print(
                    "TP: {0:F2} pips",
                    takeProfitPips
                );

                Print(
                    "Expiration: {0}",
                    expiration
                );

                Print("========================================");
            }

            // =========================================
            // BUY STOP 1
            // =========================================

            TradeResult buy1Result =
                PlaceStopOrder(
                    TradeType.Buy,
                    _tradeSymbol.Name,
                    volumeInUnits,
                    buyStop1Price,
                    BuyLabel1,
                    stopLossPips > 0
                        ? stopLossPips
                        : (double?)null,
                    takeProfitPips > 0
                        ? takeProfitPips
                        : (double?)null,
                    ProtectionType.Relative,
                    expiration,
                    "News Buy Stop 500",
                    false,
                    StopTriggerMethod.Trade
                );

            if (!buy1Result.IsSuccessful)
            {
                Print(
                    "BUY STOP 1 failed: {0}",
                    buy1Result.Error
                );
            }
            else if (Debug)
            {
                Print(
                    "BUY STOP 1 placed successfully. ID: {0}",
                    buy1Result.PendingOrder.Id
                );
            }

            // =========================================
            // BUY STOP 2
            // =========================================

            TradeResult buy2Result =
                PlaceStopOrder(
                    TradeType.Buy,
                    _tradeSymbol.Name,
                    volumeInUnits,
                    buyStop2Price,
                    BuyLabel2,
                    stopLossPips > 0
                        ? stopLossPips
                        : (double?)null,
                    takeProfitPips > 0
                        ? takeProfitPips
                        : (double?)null,
                    ProtectionType.Relative,
                    expiration,
                    "News Buy Stop 1000",
                    false,
                    StopTriggerMethod.Trade
                );

            if (!buy2Result.IsSuccessful)
            {
                Print(
                    "BUY STOP 2 failed: {0}",
                    buy2Result.Error
                );
            }
            else if (Debug)
            {
                Print(
                    "BUY STOP 2 placed successfully. ID: {0}",
                    buy2Result.PendingOrder.Id
                );
            }

            // =========================================
            // SELL STOP 1
            // =========================================

            TradeResult sell1Result =
                PlaceStopOrder(
                    TradeType.Sell,
                    _tradeSymbol.Name,
                    volumeInUnits,
                    sellStop1Price,
                    SellLabel1,
                    stopLossPips > 0
                        ? stopLossPips
                        : (double?)null,
                    takeProfitPips > 0
                        ? takeProfitPips
                        : (double?)null,
                    ProtectionType.Relative,
                    expiration,
                    "News Sell Stop 500",
                    false,
                    StopTriggerMethod.Trade
                );

            if (!sell1Result.IsSuccessful)
            {
                Print(
                    "SELL STOP 1 failed: {0}",
                    sell1Result.Error
                );
            }
            else if (Debug)
            {
                Print(
                    "SELL STOP 1 placed successfully. ID: {0}",
                    sell1Result.PendingOrder.Id
                );
            }

            // =========================================
            // SELL STOP 2
            // =========================================

            TradeResult sell2Result =
                PlaceStopOrder(
                    TradeType.Sell,
                    _tradeSymbol.Name,
                    volumeInUnits,
                    sellStop2Price,
                    SellLabel2,
                    stopLossPips > 0
                        ? stopLossPips
                        : (double?)null,
                    takeProfitPips > 0
                        ? takeProfitPips
                        : (double?)null,
                    ProtectionType.Relative,
                    expiration,
                    "News Sell Stop 1000",
                    false,
                    StopTriggerMethod.Trade
                );

            if (!sell2Result.IsSuccessful)
            {
                Print(
                    "SELL STOP 2 failed: {0}",
                    sell2Result.Error
                );
            }
            else if (Debug)
            {
                Print(
                    "SELL STOP 2 placed successfully. ID: {0}",
                    sell2Result.PendingOrder.Id
                );
            }

            _ordersPlaced = true;

            // If any of the four orders failed,
            // cancel all remaining orders so the setup
            // is not left incomplete.
            if (!buy1Result.IsSuccessful ||
                !buy2Result.IsSuccessful ||
                !sell1Result.IsSuccessful ||
                !sell2Result.IsSuccessful)
            {
                Print(
                    "One or more pending orders failed. Cancelling all remaining orders."
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

        // =============================================
        // CANCEL BOTH SELL STOPS
        // =============================================

        private void CancelSellPendingOrders()
        {
            var sellOrders =
                PendingOrders
                    .Where(order =>
                        order.SymbolName == _tradeSymbol.Name &&
                        (order.Label == SellLabel1 ||
                         order.Label == SellLabel2))
                    .ToArray();

            foreach (var order in sellOrders)
            {
                TradeResult result =
                    CancelPendingOrder(order);

                if (Debug)
                {
                    if (result.IsSuccessful)
                    {
                        Print(
                            "Cancelled SELL pending order ID {0}.",
                            order.Id
                        );
                    }
                    else
                    {
                        Print(
                            "Failed to cancel SELL pending order ID {0}: {1}",
                            order.Id,
                            result.Error
                        );
                    }
                }
            }
        }

        // =============================================
        // CANCEL BOTH BUY STOPS
        // =============================================

        private void CancelBuyPendingOrders()
        {
            var buyOrders =
                PendingOrders
                    .Where(order =>
                        order.SymbolName == _tradeSymbol.Name &&
                        (order.Label == BuyLabel1 ||
                         order.Label == BuyLabel2))
                    .ToArray();

            foreach (var order in buyOrders)
            {
                TradeResult result =
                    CancelPendingOrder(order);

                if (Debug)
                {
                    if (result.IsSuccessful)
                    {
                        Print(
                            "Cancelled BUY pending order ID {0}.",
                            order.Id
                        );
                    }
                    else
                    {
                        Print(
                            "Failed to cancel BUY pending order ID {0}: {1}",
                            order.Id,
                            result.Error
                        );
                    }
                }
            }
        }

        // =============================================
        // CANCEL ALL BOT PENDING ORDERS
        // =============================================

        private void CancelAllPendingOrders()
        {
            var pendingOrders =
                PendingOrders
                    .Where(order =>
                        order.SymbolName == _tradeSymbol.Name &&
                        (order.Label == BuyLabel1 ||
                         order.Label == BuyLabel2 ||
                         order.Label == SellLabel1 ||
                         order.Label == SellLabel2))
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
                Print(
                    "News Trading Bot stopped."
                );
        }
    }
}