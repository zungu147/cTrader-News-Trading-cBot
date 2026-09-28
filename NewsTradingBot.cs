using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.EAfricaStandardTime, AccessRights = AccessRights.None)]
    public class NewsTradingBot : Robot
    {
        private const string LabelPrefix = "NewsTradingBot_";

        private string BuyLabel => LabelPrefix + "BUY";
        private string SellLabel => LabelPrefix + "SELL";

        private bool setupPlaced;
        private bool eventHandled;

        private DateTime targetNewsTime;
        private DateTime expiryTime;

        [Parameter("Symbol", DefaultValue = "XAUUSD")]
        public string TradeSymbol { get; set; } = "XAUUSD";

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double LotSize { get; set; }

        [Parameter("News Time", DefaultValue = "2026-09-28 19:00:00")]
        public DateTime NewsTime { get; set; }

        [Parameter("Seconds Before News", DefaultValue = 3, MinValue = 1, MaxValue = 60)]
        public int SecondsBeforeNews { get; set; }

        [Parameter("Distance (Pips)", DefaultValue = 500, MinValue = 1)]
        public double DistancePips { get; set; }

        [Parameter("Stop Loss (USD)", DefaultValue = 10, MinValue = 0)]
        public double StopLossUsd { get; set; }

        [Parameter("Take Profit (USD)", DefaultValue = 20, MinValue = 0)]
        public double TakeProfitUsd { get; set; }

        [Parameter("Pending Expiration (Seconds)", DefaultValue = 60, MinValue = 1, MaxValue = 3600)]
        public int PendingExpirationSeconds { get; set; }

        [Parameter("Maximum Spread (Pips)", DefaultValue = 0, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Debug", DefaultValue = true)]
        public bool Debug { get; set; }

        private Symbol TradeSymbolObject => Symbols.GetSymbol(TradeSymbol);

        protected override void OnStart()
        {
            if (TradeSymbolObject == null)
            {
                Print("ERROR: Symbol '{0}' was not found.", TradeSymbol);
                Stop();
                return;
            }

            targetNewsTime = NewsTime;
            expiryTime = targetNewsTime.AddSeconds(PendingExpirationSeconds);

            if (targetNewsTime <= Server.Time)
            {
                Print("ERROR: News Time must be in the future.");
                Print("Server time: {0}", Server.Time);
                Stop();
                return;
            }

            Timer.Start(0.1);

            Print("========================================");
            Print("NewsTradingBot STARTED");
            Print("Symbol: {0}", TradeSymbolObject.Name);
            Print("News Time: {0}", targetNewsTime);
            Print("Order Placement: {0}",
                targetNewsTime.AddSeconds(-SecondsBeforeNews));
            Print("Distance: {0} pips", DistancePips);
            Print("Lot Size: {0}", LotSize);
            Print("SL: ${0}", StopLossUsd);
            Print("TP: ${0}", TakeProfitUsd);
            Print("Expiration: {0} seconds", PendingExpirationSeconds);
            Print("========================================");
        }

        protected override void OnTimer()
        {
            if (eventHandled)
                return;

            DateTime now = Server.Time;

            if (!setupPlaced)
            {
                DateTime placementTime =
                    targetNewsTime.AddSeconds(-SecondsBeforeNews);

                if (now >= placementTime &&
                    now < targetNewsTime.AddSeconds(1))
                {
                    PlaceNewsOrders();
                    return;
                }
            }

            if (setupPlaced &&
                now >= expiryTime &&
                !HasOurPosition())
            {
                CancelOurPendingOrders();

                eventHandled = true;

                Print("Neither pending order triggered.");
                Print("Both pending orders have been cancelled.");
            }
        }

        protected override void OnTick()
        {
            if (!setupPlaced)
                return;

            var ourPositions = Positions
                .Where(p =>
                    p.SymbolName == TradeSymbolObject.Name &&
                    (p.Label == BuyLabel || p.Label == SellLabel))
                .ToArray();

            if (ourPositions.Length > 0)
            {
                foreach (var order in PendingOrders
                    .Where(o =>
                        o.SymbolName == TradeSymbolObject.Name &&
                        (o.Label == BuyLabel || o.Label == SellLabel))
                    .ToArray())
                {
                    TradeResult result = CancelPendingOrder(order);

                    if (Debug)
                    {
                        Print(
                            "Opposite pending order {0}: {1}",
                            order.Id,
                            result.IsSuccessful
                                ? "CANCELLED"
                                : result.Error?.ToString());
                    }
                }

                eventHandled = true;
            }
        }

        private void PlaceNewsOrders()
        {
            if (setupPlaced)
                return;

            Symbol symbol = TradeSymbolObject;

            double spreadPips =
                (symbol.Ask - symbol.Bid) / symbol.PipSize;

            if (MaximumSpreadPips > 0 &&
                spreadPips > MaximumSpreadPips)
            {
                Print(
                    "Spread is {0:F1} pips, above maximum {1:F1}.",
                    spreadPips,
                    MaximumSpreadPips);

                Print("Orders were NOT placed.");

                eventHandled = true;
                return;
            }

            double volume =
                symbol.QuantityToVolumeInUnits(LotSize);

            volume =
                symbol.NormalizeVolumeInUnits(
                    volume,
                    RoundingMode.Down);

            if (volume < symbol.VolumeInUnitsMin)
            {
                Print(
                    "ERROR: Lot size {0} is below the symbol minimum.",
                    LotSize);

                eventHandled = true;
                return;
            }

            /*
             * BUY STOP:
             * Current Ask + 500 pips
             *
             * SELL STOP:
             * Current Bid - 500 pips
             */

            double buyEntry =
                symbol.Ask +
                DistancePips * symbol.PipSize;

            double sellEntry =
                symbol.Bid -
                DistancePips * symbol.PipSize;

            buyEntry = symbol.NormalizePrice(buyEntry);
            sellEntry = symbol.NormalizePrice(sellEntry);

            /*
             * Convert USD SL/TP to pips according
             * to the actual symbol and lot size.
             */

            double slPips =
                MoneyToPips(
                    StopLossUsd,
                    volume,
                    symbol);

            double tpPips =
                MoneyToPips(
                    TakeProfitUsd,
                    volume,
                    symbol);

            DateTime expiration =
                targetNewsTime.AddSeconds(
                    PendingExpirationSeconds);

            if (Debug)
            {
                Print("========================================");
                Print("NEWS ORDER SETUP");
                Print("BID: {0}", symbol.Bid);
                Print("ASK: {0}", symbol.Ask);
                Print("Spread: {0:F1} pips", spreadPips);

                Print("BUY STOP: {0}", buyEntry);
                Print("SELL STOP: {0}", sellEntry);

                Print("SL: {0:F2} pips", slPips);
                Print("TP: {0:F2} pips", tpPips);

                Print("Expiration: {0}", expiration);
                Print("========================================");
            }

            TradeResult buyResult =
                PlaceStopOrder(
                    TradeType.Buy,
                    symbol.Name,
                    volume,
                    buyEntry,
                    BuyLabel,
                    slPips > 0 ? slPips : null,
                    tpPips > 0 ? tpPips : null,
                    ProtectionType.Relative,
                    expiration,
                    "News straddle BUY",
                    false,
                    StopTriggerMethod.Trade,
                    StopTriggerMethod.Trade);

            TradeResult sellResult =
                PlaceStopOrder(
                    TradeType.Sell,
                    symbol.Name,
                    volume,
                    sellEntry,
                    SellLabel,
                    slPips > 0 ? slPips : null,
                    tpPips > 0 ? tpPips : null,
                    ProtectionType.Relative,
                    expiration,
                    "News straddle SELL",
                    false,
                    StopTriggerMethod.Trade,
                    StopTriggerMethod.Trade);

            if (!buyResult.IsSuccessful ||
                !sellResult.IsSuccessful)
            {
                Print("ERROR placing news orders.");

                Print(
                    "BUY: {0}",
                    buyResult.IsSuccessful
                        ? "SUCCESS"
                        : buyResult.Error?.ToString());

                Print(
                    "SELL: {0}",
                    sellResult.IsSuccessful
                        ? "SUCCESS"
                        : sellResult.Error?.ToString());

                /*
                 * If only one side was successfully placed,
                 * immediately remove it.
                 */

                if (buyResult.IsSuccessful &&
                    buyResult.PendingOrder != null)
                {
                    CancelPendingOrder(
                        buyResult.PendingOrder);
                }

                if (sellResult.IsSuccessful &&
                    sellResult.PendingOrder != null)
                {
                    CancelPendingOrder(
                        sellResult.PendingOrder);
                }

                eventHandled = true;
                return;
            }

            setupPlaced = true;

            Print("========================================");
            Print("SUCCESS");
            Print("BUY STOP  = {0}", buyEntry);
            Print("SELL STOP = {0}", sellEntry);
            Print("Expiration = {0}", expiration);
            Print("========================================");
        }

        private double MoneyToPips(
            double money,
            double volumeInUnits,
            Symbol symbol)
        {
            if (money <= 0)
                return 0;

            double moneyPerPipForThisVolume =
                symbol.PipValue *
                (volumeInUnits / symbol.LotSize);

            if (moneyPerPipForThisVolume <= 0)
                return 0;

            return money /
                   moneyPerPipForThisVolume;
        }

        private bool HasOurPosition()
        {
            return Positions.Any(p =>
                p.SymbolName == TradeSymbolObject.Name &&
                (p.Label == BuyLabel ||
                 p.Label == SellLabel));
        }

        private void CancelOurPendingOrders()
        {
            foreach (var order in PendingOrders
                .Where(o =>
                    o.SymbolName == TradeSymbolObject.Name &&
                    (o.Label == BuyLabel ||
                     o.Label == SellLabel))
                .ToArray())
            {
                TradeResult result =
                    CancelPendingOrder(order);

                if (Debug)
                {
                    Print(
                        "Cancel pending order {0}: {1}",
                        order.Id,
                        result.IsSuccessful
                            ? "SUCCESS"
                            : result.Error?.ToString());
                }
            }
        }

        protected override void OnStop()
        {
            CancelOurPendingOrders();

            Timer.Stop();

            Print(
                "NewsTradingBot stopped. " +
                "All remaining pending orders belonging to this bot were cancelled.");
        }
    }
}