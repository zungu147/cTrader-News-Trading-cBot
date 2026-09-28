# cTrader News Trading cBot

News straddle cBot for XAUUSD.

## Strategy

At the configured news time minus the configured lead time:

1. Read the current XAUUSD price.
2. Place a Buy Stop above the live Ask.
3. Place a Sell Stop below the live Bid.
4. Default distance is 500 pips.
5. When one pending order executes, immediately cancel the remaining opposite pending order.
6. Stop Loss and Take Profit are configured in USD.
7. Lot size is configurable.
8. Pending orders automatically expire if neither is triggered.
9. Remaining pending orders are cancelled when the cBot stops.

## Parameters

- Symbol
- Lot Size
- News Time
- Seconds Before News
- Distance (Pips)
- Stop Loss (USD)
- Take Profit (USD)
- Pending Expiration (Seconds)
- Maximum Spread (Pips)
- Debug

## Important

The bot does not automatically obtain economic-calendar events.

The trader enters the exact news release time manually.

News trading can involve extreme spread expansion, slippage, gaps, rejected orders and delayed execution.

Test the strategy on a demo account before using real funds.