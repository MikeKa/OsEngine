﻿using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;

namespace OsEngine.Indicators
{
    [Indicator("StdDevLog")]
    public class StdDevLog: Aindicator
    {
        private IndicatorDataSeries _series;

        private IndicatorParameterInt _period;

        private IndicatorParameterString _candlePoint;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _period = CreateParameterInt("Period", 20);
                _candlePoint = CreateParameterStringCollection("Candle point", "Close", Entity.CandlePointsArray);
                _series = CreateSeries("StdDevLog", Color.Green, IndicatorChartPaintType.Line, true);
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            _series.Values[index] = GetValue(candles, index);
        }
		
        private decimal GetValue(List<Candle> candles, int index)
        {
            if (index - _period.ValueInt <= 0)
            {
                return 0;
            }

            decimal sd = 0;

            int length2;
            if (index - _period.ValueInt <= _period.ValueInt) length2 = index - _period.ValueInt; else length2 = _period.ValueInt;

            decimal sum = 0;
            for (int j = index; j > index - _period.ValueInt; j--)
            {
                sum += Math.Log10(GetPoint(candles, j));
            }

            var m = sum / _period.ValueInt;

            for (int i = index; i > index - length2; i--)
            {
                decimal x = GetPoint(candles, i) - m;  //Difference between values for period and average/разница между значениями за период и средней
                double g = Math.Pow((double)x, 2.0);   // difference square/ квадрат зницы
                sd += (decimal)g;   //square footage/ сумма квадратов
            }
            sd = Math.Pow((decimal)Math.Sqrt((double)sd / length2), 10);  //find the root of sum/period // находим корень из суммы/период 

            return Math.Round(sd, 5);
        }

        private decimal GetPoint(List<Candle> candles, int index)
        {
            if (_candlePoint.ValueString == "Close")
            {
                return candles[index].Close;
            }
            if (_candlePoint.ValueString == "High")
            {
                return candles[index].High;
            }
            if (_candlePoint.ValueString == "Low")
            {
                return candles[index].Low;
            }
            if (_candlePoint.ValueString == "Open")
            {
                return candles[index].Open;
            }
            if (_candlePoint.ValueString == "Median")
            {
                return (candles[index].High + candles[index].Low) / 2;
            }
            if (_candlePoint.ValueString == "Typical")
            {
                return (candles[index].High + candles[index].Low + candles[index].Close) / 3;
            }
            return 0;
        }
    }
}