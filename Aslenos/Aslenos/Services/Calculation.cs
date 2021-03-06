﻿using Aslenos.Helpers;
using System.Collections.Generic;
using System.Threading;
using Aslenos.Models;

namespace Aslenos.Services
{
    class Calculation
    {
        private int[,] adcData;
        private int[] adcDataSize;

        private int bufferNumber;
        private int packageCount;
        private IList<int> packageCountList;

        private int packageAverageValue;
        private int samplingTime;

        private bool[] manometerUpdateFlagFirst;
        private bool[] manometerUpdateFlagSecond;

        private bool[] seriesFluctuationsFlag;
        private bool[] seriesPhaseBFlag;
        private bool[] seriesPhaseEFlag;
        private bool[] seriesPhaseFFlag;
        private bool[] graphArrayFlag;

        private double[,] graphXParams;
        private double[,] graphXParamsScreen;

        private double[] fluctuationsTime;
        private double[] seriesMaxVacuumBuff;
        private double[] totalVolume;

        private int[,] seriesXS;
        private int[,] seriesXSScreen;

        private IList<double>[] graphArrayPreBuff;
        private IList<double>[] graphArrayBuff;

        private IList<double>[] graphArray;
        private IList<double>[] graphArrayScreen;

        private int[] seriesFilter;

        private bool fluctuationAnalysFlag;

        private TimerCallback TimerCallback { get; }
        private Timer Timer { get; set; }

        private readonly DeviceDataProvider _dataProvider;

        private static int xPoint = 0;

        public Calculation()
        {
            TimerCallback = ChangeBuffer;
            InitData();
            _dataProvider = DeviceDataProvider.GetProvider;
        }

        public void AdcDataSplit(byte[] data)
        {
            packageCount++;

            if (data.Length % 2 == 0)
            {
                for (int i = 0; i < data.Length; i += 2)
                {
                    adcData[bufferNumber, adcDataSize[bufferNumber]] = (data[i + 1] << 8) + data[i];
                    adcDataSize[bufferNumber]++;

                    if (adcDataSize[bufferNumber] > Constants.MAX_BUFF_SIZE)
                    {
                        adcDataSize[bufferNumber] = 0;
                    }
                }
            }
        }

        private void ChangeBuffer(object obj)
        {
            bufferNumber ^= 1;

            if (packageCount > 0)
            {
                if (packageCountList.Count <= Constants.BUFFER_COUNT)
                {
                    packageCountList.Add(packageCount);
                    packageAverageValue += packageCount;
                }
                else
                {
                    packageAverageValue -= packageCountList[0];
                    packageCountList.RemoveAt(0);
                    packageCountList.Add(packageCount);
                    packageAverageValue += packageCount;
                }

                samplingTime = Constants.UPDATE_INTERVAL * 2 / packageAverageValue;
                packageCount = 1;
            }


            for (int i = 0; i < adcDataSize[bufferNumber ^ 1]; i++)
            {
                if (i % 2 == 0)
                {
                    var point = adcData[bufferNumber ^ 1, i];
                    _dataProvider.FirstChanel = new RealTimeDeviceData(xPoint, point);
                    FindFluctuations(point, 0);
                }
                else
                {
                    var point = adcData[bufferNumber ^ 1, i];
                    _dataProvider.SecondChanel = new RealTimeDeviceData(xPoint, point);
                    FindFluctuations(point, 1);
                }
            }

            adcDataSize[bufferNumber] = 0;
        }

        private void FindFluctuations(double point, int channel)
        {
            if (point < graphXParams[channel, GraphParams.MIN_VACUUM])
            {
                graphXParams[channel, GraphParams.MIN_VACUUM] = point;
            }
            else if (point > seriesMaxVacuumBuff[channel])
            {
                seriesMaxVacuumBuff[channel] = point;
            }

            if (fluctuationAnalysFlag)
            {
                fluctuationsTime[channel] += samplingTime;

                if (graphArrayFlag[channel])
                {
                    if (graphArrayPreBuff[channel].Count > Constants.MAX_BUFF_LENGTH)
                    {
                        graphArrayPreBuff[channel].RemoveAt(0);
                        graphArrayPreBuff[channel].Add(point);
                    }
                    else graphArrayPreBuff[channel].Add(point);
                }

                if (seriesFluctuationsFlag[channel] && point > 3.5)
                {
                    if (SeriesFilter(channel))
                    {
                        graphXParams[channel, GraphParams.PHASE_F] = fluctuationsTime[channel];
                        graphXParams[channel, GraphParams.FLUCTUATION] = graphXParams[channel, GraphParams.PHASE_F] + graphXParams[channel, GraphParams.PHASE_E];
                        fluctuationsTime[channel] = 0;

                        graphXParams[channel, GraphParams.MAX_VACUUM] = seriesMaxVacuumBuff[channel];
                        seriesMaxVacuumBuff[channel] = 0;

                        SaveTrendVolume(channel);

                        for (int i = 0; i < graphArrayPreBuff[channel].Count; i++)
                        {
                            graphArrayBuff[channel].Add(graphArrayPreBuff[channel][i]);
                        }

                        seriesXS[channel, GraphParams.FLUCTUATION] = graphArrayBuff[channel].Count;

                        seriesFluctuationsFlag[channel] = false;
                        graphArrayFlag[channel] = false;
                        seriesPhaseEFlag[channel] = true;
                        seriesPhaseBFlag[channel] = true;

                        var fluctuation = graphXParams[channel, GraphParams.FLUCTUATION];
                        var phaseA = graphXParams[channel, GraphParams.PHASE_A];
                        var phaseB = graphXParams[channel, GraphParams.PHASE_B];
                        var phaseC = graphXParams[channel, GraphParams.PHASE_C];
                        var phaseD = graphXParams[channel, GraphParams.PHASE_D];
                        var phaseE = graphXParams[channel, GraphParams.PHASE_E];
                        var phaseF = graphXParams[channel, GraphParams.PHASE_F];
                        var minVacuum = graphXParams[channel, GraphParams.MIN_VACUUM];
                        var maxVacuum = graphXParams[channel, GraphParams.MAX_VACUUM];

                        AddData(channel, fluctuation, phaseA, phaseB, phaseC, phaseD, phaseE, phaseF, minVacuum, maxVacuum);
                    }
                }

                if (graphArrayBuff[channel] != null)
                {
                    if (graphArrayBuff[channel].Count > 3000)
                    {
                        graphArrayBuff[channel].RemoveAt(0);
                        graphArrayBuff[channel].Add(point);
                    }
                    else graphArrayBuff[channel].Add(point);
                }

                if (seriesPhaseEFlag[channel] && point < (seriesMaxVacuumBuff[channel] - 4))
                {
                    graphXParams[channel, GraphParams.PHASE_E] = fluctuationsTime[channel];
                    seriesXS[channel, GraphParams.PHASE_B] = graphArrayBuff[channel].Count;
                    FindPhaseA(channel);
                    fluctuationsTime[channel] = 0;
                    seriesPhaseEFlag[channel] = false;
                    seriesPhaseFFlag[channel] = true;
                }

                if (seriesPhaseFFlag[channel] && point < 3.5)
                {
                    if (SeriesFilter(channel))
                    {
                        graphXParams[channel, GraphParams.PHASE_C] = fluctuationsTime[channel];
                        seriesXS[channel, GraphParams.PHASE_C] = graphArrayBuff[channel].Count;

                        seriesFluctuationsFlag[channel] = true;
                        seriesPhaseFFlag[channel] = false;
                        graphArrayFlag[channel] = true;
                        graphArrayPreBuff[channel].Clear();
                        graphXParams[channel, GraphParams.MIN_VACUUM] = 50.0;
                    }
                }
            }
            else
            {
                if (!manometerUpdateFlagFirst[channel])
                {
                    manometerUpdateFlagFirst[channel] = true;
                }

                totalVolume[channel] = point;

                if (manometerUpdateFlagSecond[channel])
                {
                    manometerUpdateFlagSecond[channel] = false;

                    var fluctuation = graphXParams[channel, GraphParams.FLUCTUATION];
                    var phaseA = graphXParams[channel, GraphParams.PHASE_A];
                    var phaseB = graphXParams[channel, GraphParams.PHASE_B];
                    var phaseC = graphXParams[channel, GraphParams.PHASE_C];
                    var phaseD = graphXParams[channel, GraphParams.PHASE_D];
                    var phaseE = graphXParams[channel, GraphParams.PHASE_E];
                    var phaseF = graphXParams[channel, GraphParams.PHASE_F];
                    var minVacuum = graphXParams[channel, GraphParams.MIN_VACUUM];
                    var maxVacuum = graphXParams[channel, GraphParams.MAX_VACUUM];

                    AddData(channel, fluctuation, phaseA, phaseB, phaseC, phaseD, phaseE, phaseF, minVacuum, maxVacuum);
                }
            }
        }

        private bool SeriesFilter(int channel)
        {
            seriesFilter[channel]++;

            if (seriesFilter[channel] > Constants.FILTER_NUMBER)
            {
                seriesFilter[channel] = 0;

                return true;
            }
            else
            {
                return false;
            }
        }

        private void FindPhaseA(int channel)
        {
            int maxSize = graphArrayBuff[channel].Count;
            int nullPoint = graphArrayPreBuff[channel].Count;

            int phaseELength = maxSize - nullPoint;

            double stepTime = graphXParams[channel, GraphParams.PHASE_E] / phaseELength;

            double phaseATime = 0.0;

            for (int i = nullPoint; i < maxSize; i++)
            {
                phaseATime += stepTime;

                if (graphArrayBuff[channel][i] > (seriesMaxVacuumBuff[channel] - 4))
                {
                    graphXParams[channel, GraphParams.PHASE_A] = phaseATime;
                    seriesXS[channel, GraphParams.PHASE_A] = i;

                    return;
                }
            }

        }

        private void SaveTrendVolume(int channel)
        {
            graphArray[channel].Clear();

            for (int i = 0; i < graphArrayBuff[channel].Count; i++)
            {
                graphArray[channel].Add(graphArrayBuff[channel][i]);
            }
            graphArrayBuff[channel].Clear();
        }

        public void StartCalculation(bool fluctuationFlag = false)
        {
            if (fluctuationFlag)
            {
                fluctuationAnalysFlag = true;
            }

            Timer = new Timer(TimerCallback, null, 0, Constants.UPDATE_INTERVAL);
        }

        public void StopCalculation()
        {
            Timer.Dispose();
            ResetData();
        }

        public void InitData()
        {
            ResetData();
        }

        private void ResetData()
        {
            adcData = new int[2, Constants.MAX_BUFF_SIZE];
            adcDataSize = new int[2];

            bufferNumber = 0;
            packageCount = 0;
            packageCountList = new List<int>();

            packageAverageValue = 0;
            samplingTime = 0;

            manometerUpdateFlagFirst = new bool[2];
            manometerUpdateFlagSecond = new bool[2];

            seriesFluctuationsFlag = new bool[] { true, true };
            seriesPhaseEFlag = new bool[] { false, false };
            seriesPhaseFFlag = new bool[] { false, false };
            seriesPhaseBFlag = new bool[] { false, false };
            graphArrayFlag = new bool[] { true, true };

            graphXParams = new double[2, 9];
            graphXParamsScreen = new double[2, 9];

            fluctuationsTime = new double[2];
            seriesMaxVacuumBuff = new double[2];
            totalVolume = new double[2];

            seriesXS = new int[2, 4];
            seriesXSScreen = new int[2, 4];

            graphArrayPreBuff = new List<double>[] { new List<double>(), new List<double>() };
            graphArrayBuff = new List<double>[] { new List<double>(), new List<double>() };

            graphArray = new List<double>[] { new List<double>(), new List<double>() };
            graphArrayScreen = new List<double>[] { new List<double>(), new List<double>() };

            seriesFilter = new int[2];

            fluctuationAnalysFlag = true;

            xPoint = 0;
        }

        public void AddData(int channel, params double[] data)
        {
            if (channel == 0)
            {
                var firstChanel = _dataProvider.FirstChanel;
                firstChanel.Fluctuation = data[0];
                firstChanel.PhaseA = data[1];
                firstChanel.PhaseB = data[2];
                firstChanel.PhaseC = data[3];
                firstChanel.PhaseD = data[4];
                firstChanel.PhaseE = data[5];
                firstChanel.PhaseF = data[6];
                firstChanel.MinVacuum = data[7];
                firstChanel.MaxVacuum = data[8];
            }
            else
            {
                var secondChanel = _dataProvider.SecondChanel;
                secondChanel.Fluctuation = data[0];
                secondChanel.PhaseA = data[1];
                secondChanel.PhaseB = data[2];
                secondChanel.PhaseC = data[3];
                secondChanel.PhaseD = data[4];
                secondChanel.PhaseE = data[5];
                secondChanel.PhaseF = data[6];
                secondChanel.MinVacuum = data[7];
                secondChanel.MaxVacuum = data[8];
            }
        }
    }
}
