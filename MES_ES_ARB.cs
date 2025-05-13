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
        private int tradeDirection; // 1 = ES overvalued, -1 = MES overvalued
        private const double TickSize = 0.25;
        private const double EntryThreshold = 4; // Ticks
        private const double ExitThreshold = 1;  // Ticks

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "MES/ES Directional Spread Trading";
                Name = "MESESPairsTrading";
                Calculate = Calculate.OnEachTick;
                IsUnmanaged = true;
                TraceOrders = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries("MES 06-25", BarsPeriodType.Tick, 1); // Index 0: MES
                AddDataSeries("ES 06-25", BarsPeriodType.Tick, 1);  // Index 1: ES
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
                if (e.MarketDataType == MarketDataType.Bid)
                    mesBid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask)
                    mesAsk = e.Price;
            }
            // Update ES prices
            else if (e.Instrument == Instruments[1])
            {
                if (e.MarketDataType == MarketDataType.Bid)
                    esBid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask)
                    esAsk = e.Price;
            }

            if (mesBid > 0 && mesAsk > 0 && esBid > 0 && esAsk > 0)
                ProcessStrategyLogic();
        }

        private void ProcessStrategyLogic()
        {
            // Determine overvaluation direction and calculate executable spread
            bool esOvervalued = false;
            bool mesOvervalued = false;
            double executableSpread = 0;
            double spreadTicks = 0;
            string scenario = "";

            // Check ES overvaluation (Buy MES, Sell ES)
            double esSellPrice = esBid - TickSize;
            double mesBuyPrice = mesAsk + TickSize;
            double esSpread = esSellPrice - mesBuyPrice;
            
            // Check MES overvaluation (Sell MES, Buy ES)
            double mesSellPrice = mesBid - TickSize;
            double esBuyPrice = esAsk + TickSize;
            double mesSpread = esBuyPrice - mesSellPrice;

            if (esSpread >= EntryThreshold * TickSize)
            {
                esOvervalued = true;
                executableSpread = esSpread;
                scenario = "ES Overvalued";
            }
            else if (mesSpread <= -EntryThreshold * TickSize)
            {
                mesOvervalued = true;
                executableSpread = mesSpread;
                scenario = "MES Overvalued";
            }

            spreadTicks = executableSpread / TickSize;

            // Entry Conditions
            if (tradeDirection == 0)
            {
                if (esOvervalued)
                {
                    Print($"{Time} ENTRY SIGNAL: {scenario}");
                    Print($"{Time} MES Buy Limit: {mesBuyPrice:F2} | ES Sell Limit: {esSellPrice:F2}");
                    Print($"{Time} Executable Spread: {executableSpread:F2} ({spreadTicks:N1} ticks)");
                    tradeDirection = 1;
                    SubmitEntryOrders();
                }
                else if (mesOvervalued)
                {
                    Print($"{Time} ENTRY SIGNAL: {scenario}");
                    Print($"{Time} MES Sell Limit: {mesSellPrice:F2} | ES Buy Limit: {esBuyPrice:F2}");
                    Print($"{Time} Executable Spread: {executableSpread:F2} ({spreadTicks:N1} ticks)");
                    tradeDirection = -1;
                    SubmitEntryOrders();
                }
            }
            // Exit Conditions (uses same executable spread logic)
            else
            {
                double currentSpread = 0;
                if (tradeDirection == 1) // Closing ES overvalued
                {
                    currentSpread = (esBid - TickSize) - (mesAsk + TickSize);
                }
                else if (tradeDirection == -1) // Closing MES overvalued
                {
                    currentSpread = (esAsk + TickSize) - (mesBid - TickSize);
                }
                double currentSpreadTicks = currentSpread / TickSize;

                bool shouldExit = (tradeDirection == 1 && currentSpreadTicks <= ExitThreshold) ||
                                (tradeDirection == -1 && currentSpreadTicks >= -ExitThreshold);

                if (shouldExit)
                {
                    Print($"{Time} EXIT SIGNAL");
                    Print($"{Time} Current Spread: {currentSpread:F2} ({currentSpreadTicks:N1} ticks)");
                    SubmitExitOrders();
                }
            }
        }

        private void SubmitEntryOrders()
        {
            if (tradeDirection == 1) // ES overvalued: Buy MES, Sell ES
            {
                double mesLimit = mesAsk + TickSize;
                double esLimit = esBid - TickSize;
                
                SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Limit, 10, mesLimit, 0, null, "MES_Long");
                SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Limit, 1, esLimit, 0, null, "ES_Short");
            }
            else if (tradeDirection == -1) // MES overvalued: Sell MES, Buy ES
            {
                double mesLimit = mesBid - TickSize;
                double esLimit = esAsk + TickSize;
                
                SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, 10, mesLimit, 0, null, "MES_Short");
                SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, 1, esLimit, 0, null, "ES_Long");
            }
        }

        private void SubmitExitOrders()
        {
            if (tradeDirection == 1) // Close ES overvalued
            {
                double mesLimit = mesBid - TickSize;
                double esLimit = esAsk + TickSize;
                
                SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, 10, mesLimit, 0, null, "MES_Exit");
                SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, 1, esLimit, 0, null, "ES_Exit");
            }
            else if (tradeDirection == -1) // Close MES overvalued
            {
                double mesLimit = mesAsk + TickSize;
                double esLimit = esBid - TickSize;
                
                SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Limit, 10, mesLimit, 0, null, "MES_Exit");
                SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Limit, 1, esLimit, 0, null, "ES_Exit");
            }
            tradeDirection = 0;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (orderState == OrderState.Filled)
            {
                string instrument = order.Instrument.MasterInstrument.Name;
                double slippage = Math.Abs(averageFillPrice - limitPrice)/TickSize;
                Print($"{Time} FILLED: {instrument} {order.Name} @ {averageFillPrice} (Slippage: {slippage:N1} ticks)");
            }
        }
    }
}
