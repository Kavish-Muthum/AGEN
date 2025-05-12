#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MESESPairsTrading : Strategy
    {
        private double mesBid;
        private double mesAsk;
        private double esBid;
        private double esAsk;
        private int tradeDirection; 
        private const double TickSize = 0.25;
        private const double EntryThreshold = 6; 
        private const double ExitThreshold = 1;  

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "MES/ES Directional Spread Trading";
                Name = "MESESPairsTrading";
                Calculate = Calculate.OnEachTick;
                IsUnmanaged = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries("MES 06-25", BarsPeriodType.Tick, 1);
                AddDataSeries("ES 06-25", BarsPeriodType.Tick, 1);
            }
            else if (State == State.Terminated)
            {
                tradeDirection = 0;
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Update MES prices
            if (e.Instrument == Instruments[0])
            {
                if (e.MarketDataType == MarketDataType.Bid) mesBid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask) mesAsk = e.Price;
            }
            // Update ES prices
            else if (e.Instrument == Instruments[1])
            {
                if (e.MarketDataType == MarketDataType.Bid) esBid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask) esAsk = e.Price;
            }

            if (mesBid > 0 && mesAsk > 0 && esBid > 0 && esAsk > 0)
                ProcessStrategyLogic();
        }

        private void ProcessStrategyLogic()
        {
            double currentSpread = esBid - mesAsk; // Positive = ES overvalued, Negative = MES overvalued
            double spreadTicks = currentSpread / TickSize;

            Print($"{Time} - Spread: {spreadTicks:N1} ticks | Direction: {(currentSpread > 0 ? "ES Overvalued" : "MES Overvalued")}");

            // Entry Conditions
            if (tradeDirection == 0)
            {
                if (spreadTicks >= EntryThreshold) // ES overvalued
                {
                    tradeDirection = 1;
                    SubmitEntryOrders();
                }
                else if (spreadTicks <= -EntryThreshold) // MES overvalued
                {
                    tradeDirection = -1;
                    SubmitEntryOrders();
                }
            }
            // Exit Conditions
            else
            {
                bool shouldExit = (tradeDirection == 1 && spreadTicks <= ExitThreshold) ||
                                 (tradeDirection == -1 && spreadTicks >= -ExitThreshold);

                if (shouldExit) SubmitExitOrders();
            }
        }

        private void SubmitEntryOrders()
        {
            if (tradeDirection == 1) // ES overvalued: Buy MES, Sell ES
            {
                SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, 10, mesAsk + TickSize, 0, null, "MES_Long");
                SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, 1, esBid - TickSize, 0, null, "ES_Short");
            }
            else if (tradeDirection == -1) // MES overvalued: Sell MES, Buy ES
            {
                SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Limit, 10, mesBid - TickSize, 0, null, "MES_Short");
                SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Limit, 1, esAsk + TickSize, 0, null, "ES_Long");
            }
        }

        private void SubmitExitOrders()
        {
            if (tradeDirection == 1) // Close ES overvalued position
            {
                SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Limit, 10, mesBid - TickSize, 0, null, "MES_Exit");
                SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Limit, 1, esAsk + TickSize, 0, null, "ES_Exit");
            }
            else if (tradeDirection == -1) // Close MES overvalued position
            {
                SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, 10, mesAsk + TickSize, 0, null, "MES_Exit");
                SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, 1, esBid - TickSize, 0, null, "ES_Exit");
            }
           
            tradeDirection = 0;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (orderState == OrderState.Filled)
            {
                double slippage = Math.Abs(averageFillPrice - limitPrice);
                Print($"{Time} - {order.Name} filled @ {averageFillPrice} (Slippage: {slippage/TickSize:N2} ticks)");
            }
        }
    }
}