using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System.Linq;
using OsEngine.Logging;
using System.Windows.Media.Animation;

/* Description
trading robot for osengine

The Countertrend robot on Bollinger

Buy:
1. The price is below or equal to the lower bollinger band.
2. Standard Deviation is higher than MinValue.
Sell:
1. The price is above or equal to the upper bollinger band.
2. Standard Deviation is higher than MinValue.
Exit from buy: The trailing stop is placed at the minimum for the period specified for the trailing stop and transferred (slides) to new price lows, also for the specified period.
Exit from sell: The trailing stop is placed at the maximum for the period specified for the trailing stop and is transferred (slides) to the new maximum of the price, also for the specified period.

 */


namespace OsEngine.Robots.AO
{
    [Bot("CountertrendBollingerAndStdDevLog")] // We create an attribute so that we don't write anything to the BotFactory
    public class CountertrendBollingerAndStdDevLog : BotPanel
    {
        private BotTabSimple _tab;

        // Basic Settings
        private StrategyParameterString Regime;
        private StrategyParameterString VolumeRegime;
        private StrategyParameterDecimal VolumeOnPosition;
        private StrategyParameterDecimal Slippage;
        private StrategyParameterTimeOfDay StartTradeTime;
        private StrategyParameterTimeOfDay EndTradeTime;

        // Indicator setting 
        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt LengthStdDevLog;
        private StrategyParameterDecimal MinValue;

        // Indicator
        Aindicator _BollingerLog;
        Aindicator _StdDevLog;

        // The last value of the indicator
        private decimal _lastUpLine;
        private decimal _lastDownLine;
        private decimal _lastSD;

        // The prev value of the indicator
        private decimal _prevUpLine;
        private decimal _prevDownLine;

        // Exit
        private StrategyParameterString TrailRegime;
        private StrategyParameterInt TrailCandlesLong;
        private StrategyParameterInt TrailCandlesShort;

        public CountertrendBollingerAndStdDevLog(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // Basic setting
            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency" }, "Base");
            VolumeOnPosition = CreateParameter("Volume", 1, 1.0m, 50, 4, "Base");
            Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");
            StartTradeTime = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
            EndTradeTime = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

            // Indicator setting
            BollingerLength = CreateParameter("Bollinger Length", 21, 7, 48, 7, "Indicator");
            BollingerDeviation = CreateParameter("Bollinger Deviation", 1.0m, 1, 5, 0.1m, "Indicator");
            LengthStdDevLog = CreateParameter("StdDevLog Length", 14, 7, 48, 7, "Indicator");
            MinValue = CreateParameter("MinValue", 0.2m, 10, 200, 10, "Indicator");

            // Create indicator Bollinger
            _BollingerLog = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            _BollingerLog = (Aindicator)_tab.CreateCandleIndicator(_BollingerLog, "Prime");
            ((IndicatorParameterInt)_BollingerLog.Parameters[0]).ValueInt = BollingerLength.ValueInt;
            ((IndicatorParameterDecimal)_BollingerLog.Parameters[1]).ValueDecimal = BollingerDeviation.ValueDecimal;
            _BollingerLog.Save();

            // Create indicator RSI
            _StdDevLog = IndicatorsFactory.CreateIndicatorByName("StdDevLog", name + "StdDevLog", false);
            _StdDevLog = (Aindicator)_tab.CreateCandleIndicator(_StdDevLog, "NewArea");
            ((IndicatorParameterInt)_StdDevLog.Parameters[0]).ValueInt = LengthStdDevLog.ValueInt;
            _StdDevLog.Save();

            // Exit
            TrailRegime = CreateParameter("Trail Regime", "Off", new[] {"Off", "On"}, "Exit");
            TrailCandlesLong = CreateParameter("Trail Candles Long", 5, 5, 200, 5, "Exit");
            TrailCandlesShort = CreateParameter("Trail Candles Short", 5, 5, 200, 5, "Exit");

            // Subscribe to the indicator update event
            ParametrsChangeByUser += CountertrendBollingerAndStdDevLog_ParametrsChangeByUser; ;

            // Subscribe to the candle finished event
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

            Description = "The Countertrend robot on Bollinger. " +
                "Buy: " +
                "1. The price is below or equal to the lower bollinger band. " +
                "2. Standard Deviation is higher than MinValue. " +
                "Sell: " +
                "1. The price is above or equal to the upper bollinger band. " +
                "2. Standard Deviation is higher than MinValue. " +
                "Exit from buy: The trailing stop is placed at the minimum for the period specified for the trailing stop and transferred (slides) to new price lows, also for the specified period. " +
                "Exit from sell: The trailing stop is placed at the maximum for the period specified for the trailing stop and is transferred (slides) to the new maximum of the price, also for the specified period.";
        }

        private void CountertrendBollingerAndStdDevLog_ParametrsChangeByUser()
        {
            ((IndicatorParameterInt)_BollingerLog.Parameters[0]).ValueInt = BollingerLength.ValueInt;
            ((IndicatorParameterDecimal)_BollingerLog.Parameters[1]).ValueDecimal = BollingerDeviation.ValueDecimal;
            _BollingerLog.Save();
            _BollingerLog.Reload();
            ((IndicatorParameterInt)_StdDevLog.Parameters[0]).ValueInt = LengthStdDevLog.ValueInt;
            _StdDevLog.Save();
            _StdDevLog.Reload();
        }

        // The name of the robot in OsEngine
        public override string GetNameStrategyType()
        {
            return "CountertrendBollingerAndStdDevLog";
        }
        public override void ShowIndividualSettingsDialog()
        {

        }

        // Candle Finished Event
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            // If the robot is turned off, exit the event handler
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // If there are not enough candles to build an indicator, we exit
            if (candles.Count < BollingerDeviation.ValueDecimal ||
                candles.Count < BollingerLength.ValueInt)
            {
                return;
            }

            // If the time does not match, we leave
            if (StartTradeTime.Value > _tab.TimeServerCurrent ||
                EndTradeTime.Value < _tab.TimeServerCurrent)
            {
                return;
            }

            List<Position> openPositions = _tab.PositionsOpenAll;

            // If there are positions, then go to the position closing method
            if (openPositions != null && openPositions.Count != 0)
            {
                LogicClosePosition(candles);
            }

            // If the position closing mode, then exit the method
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }
            // If there are no positions, then go to the position opening method

            LogicOpenPosition(candles);
            
        }

        // Opening logic
        private void LogicOpenPosition(List<Candle> candles)
        {

            // The last value of the indicator
            //
            List<decimal> _upLine = _BollingerLog.DataSeries[0].Values;
            List<decimal> _downLine = _BollingerLog.DataSeries[1].Values;


            //_upLine = _BollingerLog.DataSeries[0].Values;
            _lastUpLine = _upLine[_upLine.Count - 1];
            _prevUpLine = _upLine[_upLine.Count - 2];

            //_downLine = _BollingerLog.DataSeries[1].Values;
            _lastDownLine = _downLine[_downLine.Count - 1];
            _prevDownLine = _downLine[_downLine.Count - 2];

            //Std
            _lastSD = _StdDevLog.DataSeries[0].Last;

            //Price
            decimal lastPrice = candles[candles.Count - 1].Close;
            decimal prevPrice = candles[candles.Count - 2].Close;

            // Slippage
            decimal _slippage = Slippage.ValueDecimal * _tab.Securiti.PriceStep;

            //Price
            bool long_f = false;
            bool short_f = false;
            
            List<Position> openPositions = _tab.PositionsOpenAll;
            
            if (openPositions != null && openPositions.Count > 0)
            {
                for (int i = 0; i < openPositions.Count; i++)
                {
                    if (openPositions[i].Direction == Side.Buy)
                    {
                        long_f = true;
                    }
                    if (openPositions[i].Direction == Side.Sell)
                    {
                        short_f = true;
                    }
                }
                
            }

            // Long
            if (Regime.ValueString != "OnlyShort") // If the mode is not only short, then we enter long
            {
                if (_lastDownLine <= lastPrice && _prevDownLine > prevPrice && long_f == false)// _lastSD > MinValue.ValueDecimal)
                {
                    _tab.BuyAtMarket(GetVolume());
                    //_tab.BuyAtLimit(GetVolume(), _tab.PriceBestAsk + _slippage);
                }
            }

            // Short
            if (Regime.ValueString != "OnlyLong") // If the mode is not only long, then we enter short
            {
                if (_lastUpLine >= lastPrice && _prevUpLine < prevPrice && short_f == false)// _lastSD > MinValue.ValueDecimal)
                {
                    _tab.SellAtMarket(GetVolume());
                    //_tab.SellAtLimit(GetVolume(), _tab.PriceBestBid - _slippage);
                }
            }
        }

        // Logic close position
        private void LogicClosePosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;

            decimal _slippage = Slippage.ValueDecimal * _tab.Securiti.PriceStep;

            decimal lastPrice = candles[candles.Count - 1].Close;
            decimal prevPrice = candles[candles.Count - 2].Close;

            //
            List<decimal> _upLine = _BollingerLog.DataSeries[0].Values;
            List<decimal> _downLine = _BollingerLog.DataSeries[1].Values;

            //_upLine = _BollingerLog.DataSeries[0].Values;
            _lastUpLine = _upLine[_upLine.Count - 1];
            _prevUpLine = _upLine[_upLine.Count - 2];

            //_downLine = _BollingerLog.DataSeries[1].Values;
            _lastDownLine = _downLine[_downLine.Count - 1];
            _prevDownLine = _downLine[_downLine.Count - 2];

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position position = openPositions[i];

                /*
                if (position.State != PositionStateType.Open)
                {
                    continue;
                }
                */

                if (position.Direction == Side.Buy) // If the direction of the position is purchase
                {
                    if (_lastUpLine >= lastPrice && _prevUpLine < prevPrice)// _lastSD > MinValue.ValueDecimal)
                    {
                        _tab.CloseAtMarket(position, position.OpenVolume);
                        //return;
                    }
                    
                    if (TrailRegime.ValueString == "On")
                    {
                        decimal price = GetPriceStop(Side.Buy, candles, candles.Count - 1);
                        if (price == 0)
                            {
                                return; 
                            }
                        _tab.CloseAtTrailingStop(position, price, price - _slippage);
                    }
                    
                }
                else // If the direction of the position is sale
                {
                    if (_lastDownLine <= lastPrice && _prevDownLine > prevPrice)// _lastSD > MinValue.ValueDecimal)
                        {
                            _tab.CloseAtMarket(position, position.OpenVolume);
                            //return;
                        }
                    
                    if (TrailRegime.ValueString == "On")
                    {
                        decimal price = GetPriceStop(Side.Sell, candles, candles.Count - 1);
                        if (price == 0)
                            {
                                return;
                            }
                        _tab.CloseAtTrailingStop(position, price, price + _slippage);
                    }
                }

            }
        }

        private decimal GetPriceStop(Side side, List<Candle> candles, int index)
        {
            if (candles == null || index < TrailCandlesLong.ValueInt || index < TrailCandlesShort.ValueInt)
            {
                return 0;
            }

            if (side == Side.Buy)
            {
                decimal price = decimal.MaxValue;

                for (int i = index; i > index - TrailCandlesLong.ValueInt; i--)
                {
                    if (candles[i].Low < price)
                    {
                        price = candles[i].Low;
                    }
                }
                return price;
            }

            if (side == Side.Sell)
            {
                decimal price = 0;

                for (int i = index; i > index - TrailCandlesShort.ValueInt; i--)
                {
                    if (candles[i].High > price)
                    {
                        price = candles[i].High;
                    }
                }

                return price;
            }
            return 0;
        }

        // Method for calculating the volume of entry into a position
        private decimal GetVolume()
        {
            decimal volume = 0;

            if (VolumeRegime.ValueString == "Contract currency")
            {
                decimal contractPrice = _tab.PriceBestAsk;
                volume = VolumeOnPosition.ValueDecimal / contractPrice;
            }
            else if (VolumeRegime.ValueString == "Number of contracts")
            {
                volume = VolumeOnPosition.ValueDecimal;
            }

            // If the robot is running in the tester
            if (StartProgram == StartProgram.IsTester)
            {
                volume = Math.Round(volume, 6);
            }
            else
            {
                volume = Math.Round(volume, _tab.Securiti.DecimalsVolume);
            }
            return volume;
        }
    }
}
