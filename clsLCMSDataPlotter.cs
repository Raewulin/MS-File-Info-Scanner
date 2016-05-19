﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace MSFileInfoScanner
{
    /// <summary>
    /// This class tracks the m/z and intensity values for a series of spectra
    /// It can then create a 2D plot of m/z vs. intensity
    /// To keep the plot from being too dense, it will filter the data to show at most MaxPointsToPlot data points
    /// Furthermore, it will bin the data by MZResolution m/z units (necessary if the data is not centroided)
    /// </summary>
    /// <remarks></remarks>
    public class clsLCMSDataPlotter
    {

        #region "Constants, Enums, Structures"

        // Absolute maximum number of ions that will be tracked for a mass spectrum
        private const int MAX_ALLOWABLE_ION_COUNT = 50000;

        public enum eOutputFileTypes
        {
            LCMS = 0,
            LCMSMSn = 1
        }

        private struct udtOutputFileInfoType
        {
            public eOutputFileTypes FileType;
            public string FileName;
            public string FilePath;
        }

        public struct udtMSIonType
        {
            public double MZ;
            public double Intensity;

            public byte Charge;
            public override string ToString()
            {
                if (Charge > 0)
                {
                    return MZ.ToString("0.000") + ", " + Intensity.ToString("0") + ", " + Charge + "+";
                }
                else
                {
                    return MZ.ToString("0.000") + ", " + Intensity.ToString("0");
                }
            }
        }

        #endregion

        #region "Member variables"

        // Keeps track of the total number of data points cached in mScans
        private int mPointCountCached;

        private int mPointCountCachedAfterLastTrim;

        private List<clsScanData> mScans;

        private MSFileInfoScannerInterfaces.clsLCMSDataPlotterOptions mOptions;

        private readonly List<udtOutputFileInfoType> mRecentFiles;
        public event ErrorEventEventHandler ErrorEvent;
        public delegate void ErrorEventEventHandler(string Message);

        private int mSortingWarnCount;
        private int mSpectraFoundExceedingMaxIonCount;
        private int mMaxIonCountReported;

        #endregion

        #region "Properties"
        public MSFileInfoScannerInterfaces.clsLCMSDataPlotterOptions Options
        {
            get { return mOptions; }
            set { mOptions = value; }
        }

        public int ScanCountCached
        {
            get { return mScans.Count; }
        }

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public clsLCMSDataPlotter()
            : this(new MSFileInfoScannerInterfaces.clsLCMSDataPlotterOptions())
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="objOptions"></param>
        public clsLCMSDataPlotter(MSFileInfoScannerInterfaces.clsLCMSDataPlotterOptions objOptions)
        {
            mOptions = objOptions;
            mRecentFiles = new List<udtOutputFileInfoType>();
            mSortingWarnCount = 0;
            mSpectraFoundExceedingMaxIonCount = 0;
            mMaxIonCountReported = 0;
        }

        private void AddRecentFile(string strFilePath, eOutputFileTypes eFileType)
        {
            var udtOutputFileInfo = new udtOutputFileInfoType
            {
                FileType = eFileType,
                FileName = Path.GetFileName(strFilePath),
                FilePath = strFilePath
            };


            mRecentFiles.Add(udtOutputFileInfo);
        }
        

        public bool AddScan2D(int intScanNumber, int intMSLevel, float sngScanTimeMinutes, int intIonCount, double[,] dblMassIntensityPairs)
        {
            try
            {
                if (intIonCount <= 0)
                {
                    // No data to add
                    return false;
                }

                // Make sure the data is sorted by m/z
                var intIndex = 0;
                for (intIndex = 1; intIndex <= intIonCount - 1; intIndex++)
                {
                    // Note that dblMassIntensityPairs[0, intIndex) is m/z
                    //       and dblMassIntensityPairs[1, intIndex) is intensity
                    if (dblMassIntensityPairs[0, intIndex] < dblMassIntensityPairs[0, intIndex - 1])
                    {
                        // May need to sort the data
                        // However, if the intensity of both data points is zero, then we can simply swap the data
                        if (Math.Abs(dblMassIntensityPairs[1, intIndex]) < double.Epsilon && Math.Abs(dblMassIntensityPairs[1, intIndex - 1]) < double.Epsilon)
                        {
                            // Swap the m/z values
                            double dblSwapVal = dblMassIntensityPairs[0, intIndex];
                            dblMassIntensityPairs[0, intIndex] = dblMassIntensityPairs[0, intIndex - 1];
                            dblMassIntensityPairs[0, intIndex - 1] = dblSwapVal;
                        }
                        else
                        {
                            // Need to sort
                            mSortingWarnCount += 1;
                            if (mSortingWarnCount <= 10)
                            {
                                Console.WriteLine("  Sorting m/z data (this typically shouldn't be required for Finnigan data, though can occur for high res orbitrap data)");
                            }
                            else if (mSortingWarnCount % 100 == 0)
                            {
                                Console.WriteLine("  Sorting m/z data (i = " + mSortingWarnCount + ")");
                            }

                            // We can't easily sort a 2D array in .NET
                            // Thus, we must copy the data into new arrays and then call AddScan()

                            var lstIons = new List<udtMSIonType>(intIonCount - 1);

                            for (var intCopyIndex = 0; intCopyIndex <= intIonCount - 1; intCopyIndex++)
                            {
                                var udtIon = new udtMSIonType
                                {
                                    MZ = dblMassIntensityPairs[0, intCopyIndex],
                                    Intensity = dblMassIntensityPairs[1, intCopyIndex]
                                };


                                lstIons.Add(udtIon);
                            }

                            return AddScan(intScanNumber, intMSLevel, sngScanTimeMinutes, lstIons);

                        }
                    }
                }


                double[] dblIonsMZFiltered = null;
                float[] sngIonsIntensityFiltered = null;
                byte[] bytChargeFiltered = null;

                dblIonsMZFiltered = new double[intIonCount];
                sngIonsIntensityFiltered = new float[intIonCount];
                bytChargeFiltered = new byte[intIonCount];

                // Populate dblIonsMZFiltered & sngIonsIntensityFiltered, skipping any data points with an intensity value of 0 or less than mMinIntensity

                var intIonCountNew = 0;
                for (intIndex = 0; intIndex <= intIonCount - 1; intIndex++)
                {
                    if (dblMassIntensityPairs[1, intIndex] > 0 && dblMassIntensityPairs[1, intIndex] >= mOptions.MinIntensity)
                    {
                        dblIonsMZFiltered[intIonCountNew] = dblMassIntensityPairs[0, intIndex];

                        if (dblMassIntensityPairs[1, intIndex] > float.MaxValue)
                        {
                            sngIonsIntensityFiltered[intIonCountNew] = float.MaxValue;
                        }
                        else
                        {
                            sngIonsIntensityFiltered[intIonCountNew] = Convert.ToSingle(dblMassIntensityPairs[1, intIndex]);
                        }

                        bytChargeFiltered[intIonCountNew] = 0;

                        intIonCountNew += 1;
                    }
                }

                const bool USE_LOG = false;
                if (USE_LOG)
                {
                    for (intIndex = 0; intIndex <= intIonCountNew - 1; intIndex++)
                    {
                        if (sngIonsIntensityFiltered[intIndex] > 0)
                        {
                            sngIonsIntensityFiltered[intIndex] = Convert.ToSingle(Math.Log10(sngIonsIntensityFiltered[intIndex]));
                        }
                    }
                }

                AddScanCheckData(intScanNumber, intMSLevel, sngScanTimeMinutes, intIonCountNew, dblIonsMZFiltered, sngIonsIntensityFiltered, bytChargeFiltered);


            }
            catch (Exception ex)
            {
                if (ErrorEvent != null)
                {
                    ErrorEvent("Error in clsLCMSDataPlotter.AddScan2D: " + ex.Message + "; inner exception: " + ex.InnerException.Message);
                }
                return false;
            }

            return true;

        }

        public bool AddScan(int intScanNumber, int intMSLevel, float sngScanTimeMinutes, int intIonCount, double[] dblIonsMZ, double[] dblIonsIntensity)
        {
            List<udtMSIonType> lstIons;

            if (intIonCount > MAX_ALLOWABLE_ION_COUNT) {
                Array.Sort(dblIonsIntensity, dblIonsMZ);

                var lstHighIntensityIons = new List<udtMSIonType>(MAX_ALLOWABLE_ION_COUNT);

                for (int intIndex = intIonCount - MAX_ALLOWABLE_ION_COUNT; intIndex <= intIonCount - 1; intIndex++) {
                    var udtIon = new udtMSIonType
                    {
                        MZ = dblIonsMZ[intIndex],
                        Intensity = dblIonsIntensity[intIndex]
                    };


                    lstHighIntensityIons.Add(udtIon);
                }

                lstIons = (from item in lstHighIntensityIons orderby item.MZ select item).ToList();

            } else {
                lstIons = new List<udtMSIonType>(intIonCount - 1);

                for (var intIndex = 0; intIndex <= intIonCount - 1; intIndex++) {
                    var udtIon = new udtMSIonType
                    {
                        MZ = dblIonsMZ[intIndex],
                        Intensity = dblIonsIntensity[intIndex]
                    };


                    lstIons.Add(udtIon);
                }
            }

            return AddScan(intScanNumber, intMSLevel, sngScanTimeMinutes, lstIons);

        }

        public bool AddScan(int intScanNumber, int intMSLevel, float sngScanTimeMinutes, List<udtMSIonType> lstIons)
        {
            try
            {
                if (lstIons.Count == 0)
                {
                    // No data to add
                    return false;
                }

                // Make sure the data is sorted by m/z
                var intIndex = 0;
                for (intIndex = 1; intIndex <= lstIons.Count - 1; intIndex++)
                {
                    if (!(lstIons[intIndex].MZ < lstIons[intIndex - 1].MZ))
                    {
                        continue;
                    }

                    // May need to sort the data
                    // However, if the intensity of both data points is zero, then we can simply swap the data
                    if (Math.Abs(lstIons[intIndex].Intensity - 0) < double.Epsilon && Math.Abs(lstIons[intIndex - 1].Intensity - 0) < double.Epsilon)
                    {
                        // Swap the m/z values
                        var udtSwapVal = lstIons[intIndex];
                        lstIons[intIndex] = lstIons[intIndex - 1];
                        lstIons[intIndex - 1] = udtSwapVal;
                    }
                    else
                    {
                        // Need to sort
                        mSortingWarnCount += 1;
                        if (mSortingWarnCount <= 10)
                        {
                            Console.WriteLine("  Sorting m/z data (this typically shouldn't be required for Finnigan data, though can occur for high res orbitrap data)");
                        }
                        else if (mSortingWarnCount % 100 == 0)
                        {
                            Console.WriteLine("  Sorting m/z data (i = " + mSortingWarnCount + ")");
                        }
                        lstIons.Sort(new udtMSIonTypeComparer());
                        break;// TODO: might not be correct. Was : Exit For
                    }
                }


                double[] dblIonsMZFiltered = null;
                float[] sngIonsIntensityFiltered = null;
                byte[] bytCharge = null;

                dblIonsMZFiltered = new double[lstIons.Count];
                sngIonsIntensityFiltered = new float[lstIons.Count];
                bytCharge = new byte[lstIons.Count];

                // Populate dblIonsMZFiltered & sngIonsIntensityFiltered, skipping any data points with an intensity value of 0 or less than mMinIntensity

                var intIonCountNew = 0;
                for (intIndex = 0; intIndex <= lstIons.Count - 1; intIndex++)
                {
                    if (lstIons[intIndex].Intensity > 0 && lstIons[intIndex].Intensity >= mOptions.MinIntensity)
                    {
                        dblIonsMZFiltered[intIonCountNew] = lstIons[intIndex].MZ;

                        if (lstIons[intIndex].Intensity > float.MaxValue)
                        {
                            sngIonsIntensityFiltered[intIonCountNew] = float.MaxValue;
                        }
                        else
                        {
                            sngIonsIntensityFiltered[intIonCountNew] = Convert.ToSingle(lstIons[intIndex].Intensity);
                        }

                        bytCharge[intIonCountNew] = lstIons[intIndex].Charge;

                        intIonCountNew += 1;
                    }
                }

                AddScanCheckData(intScanNumber, intMSLevel, sngScanTimeMinutes, intIonCountNew, dblIonsMZFiltered, sngIonsIntensityFiltered, bytCharge);

            }
            catch (Exception ex)
            {
                if (ErrorEvent != null)
                {
                    ErrorEvent("Error in clsLCMSDataPlotter.AddScan: " + ex.Message + "; inner exception: " + ex.InnerException.Message);
                }
                return false;
            }

            return true;

        }

        private void AddScanCheckData(int intScanNumber, int intMSLevel, float sngScanTimeMinutes, int intIonCount, double[] dblIonsMZFiltered, float[] sngIonsIntensityFiltered, byte[] bytChargeFiltered)
        {
            
            var intMaxAllowableIonCount = 0;
            var blnCentroidRequired = false;
            var intIndex = 0;

            // Check whether any of the data points is less than mOptions.MZResolution m/z units apart
            blnCentroidRequired = false;
            for (intIndex = 0; intIndex <= intIonCount - 2; intIndex++)
            {
                if (dblIonsMZFiltered[intIndex + 1] - dblIonsMZFiltered[intIndex] < mOptions.MZResolution)
                {
                    blnCentroidRequired = true;
                    break; // TODO: might not be correct. Was : Exit For
                }
            }

            if (blnCentroidRequired)
            {
                // Consolidate any points closer than mOptions.MZResolution m/z units
                CentroidMSData(mOptions.MZResolution, ref intIonCount, ref dblIonsMZFiltered, ref sngIonsIntensityFiltered, ref bytChargeFiltered);
            }

            // Instantiate a new ScanData object for this scan
            var objScanData = new clsScanData(intScanNumber, intMSLevel, sngScanTimeMinutes, intIonCount, dblIonsMZFiltered, sngIonsIntensityFiltered, bytChargeFiltered);

            intMaxAllowableIonCount = MAX_ALLOWABLE_ION_COUNT;
            if (objScanData.IonCount > intMaxAllowableIonCount)
            {
                // Do not keep more than 50,000 ions
                mSpectraFoundExceedingMaxIonCount += 1;

                // Display a message at the console the first 10 times we encounter spectra with over intMaxAllowableIonCount ions
                // In addition, display a new message every time a new max value is encountered
                if (mSpectraFoundExceedingMaxIonCount <= 10 || objScanData.IonCount > mMaxIonCountReported)
                {
                    Console.WriteLine();
                    Console.WriteLine("Note: Scan " + intScanNumber + " has " + objScanData.IonCount + " ions; will only retain " + intMaxAllowableIonCount + " (trimmed " + mSpectraFoundExceedingMaxIonCount.ToString + " spectra)");

                    mMaxIonCountReported = objScanData.IonCount;
                }

                DiscardDataToLimitIonCount(ref objScanData, 0, 0, intMaxAllowableIonCount);
            }

            mScans.Add(objScanData);
            mPointCountCached += objScanData.IonCount;

            if (mPointCountCached > mOptions.MaxPointsToPlot * 5)
            {
                // Too many data points are being tracked; trim out the low abundance ones

                // However, only repeat the trim if the number of cached data points has increased by 10%
                // This helps speed up program execution by avoiding trimming data after every new scan is added

                if (mPointCountCached > mPointCountCachedAfterLastTrim * 1.1)
                {
                    // Step through the scans and reduce the number of points in memory
                    TrimCachedData(mOptions.MaxPointsToPlot, mOptions.MinPointsPerSpectrum);

                }
            }

        }

        public bool AddScanSkipFilters(ref clsScanData objSourceData)
        {

            var blnSuccess = false;

            try
            {
                if (objSourceData == null || objSourceData.IonCount <= 0)
                {
                    // No data to add
                    return false;
                }

                // Copy the data in objSourceScan
                var objScanData = new clsScanData(objSourceData.ScanNumber, objSourceData.MSLevel, objSourceData.ScanTimeMinutes, objSourceData.IonCount, objSourceData.IonsMZ, objSourceData.IonsIntensity, objSourceData.Charge);

                mScans.Add(objScanData);
                mPointCountCached += objScanData.IonCount;

                if (mPointCountCached > mOptions.MaxPointsToPlot * 5)
                {
                    // Too many data points are being tracked; trim out the low abundance ones

                    // However, only repeat the trim if the number of cached data points has increased by 10%
                    // This helps speed up program execution by avoiding trimming data after every new scan is added

                    if (mPointCountCached > mPointCountCachedAfterLastTrim * 1.1)
                    {
                        // Step through the scans and reduce the number of points in memory
                        TrimCachedData(mOptions.MaxPointsToPlot, mOptions.MinPointsPerSpectrum);

                    }
                }

            }
            catch (Exception ex)
            {
                if (ErrorEvent != null)
                {
                    ErrorEvent("Error in clsLCMSDataPlotter.AddScanSkipFilters: " + ex.Message + "; inner exception: " + ex.InnerException.Message);
                }
                blnSuccess = false;
            }

            return blnSuccess;

        }

        public void ClearRecentFileInfo()
        {
            mRecentFiles.Clear();
        }

        public float ComputeAverageIntensityAllScans(int intMSLevelFilter)
        {

            int intScanIndex = 0;
            int intIonIndex = 0;

            int intDataCount = 0;
            double dblIntensitySum = 0;

            if (intMSLevelFilter > 0)
            {
                ValidateMSLevel();
            }

            if (mPointCountCached > mOptions.MaxPointsToPlot)
            {
                // Need to step through the scans and reduce the number of points in memory

                // Note that the number of data points remaining after calling this function may still be
                //  more than mOptions.MaxPointsToPlot, depending on mOptions.MinPointsPerSpectrum 
                //  (see TrimCachedData for more details)

                TrimCachedData(mOptions.MaxPointsToPlot, mOptions.MinPointsPerSpectrum);

            }


            intDataCount = 0;
            dblIntensitySum = 0;

            for (intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++)
            {

                if (intMSLevelFilter == 0 || mScans[intScanIndex].MSLevel == intMSLevelFilter)
                {
                    for (intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++)
                    {
                        dblIntensitySum += mScans[intScanIndex].IonsIntensity[intIonIndex];
                        intDataCount += 1;
                    }
                }
            }

            if (intDataCount > 0)
            {
                return Convert.ToSingle(dblIntensitySum / intDataCount);
            }
            else
            {
                return 0;
            }
        }


        private void CentroidMSData(float sngMZResolution, ref int intIonCount, ref double[] dblIonsMZ, ref float[] sngIonsIntensity, ref byte[] bytChargeFiltered)
        {
            float[] sngIntensitySorted = null;
            int[] intPointerArray = null;

            int intIndex = 0;
            int intIndexAdjacent = 0;
            int intPointerIndex = 0;
            int intIonCountNew = 0;

            if (sngMZResolution <= 0)
            {
                // Nothing to do
                return;
            }

            try
            {
                sngIntensitySorted = new float[intIonCount];
                intPointerArray = new int[intIonCount];

                for (intIndex = 0; intIndex <= intIonCount - 1; intIndex++)
                {
                    if (sngIonsIntensity[intIndex] < 0)
                    {
                        // Do not allow for negative intensities; change it to 0
                        sngIonsIntensity[intIndex] = 0;
                    }
                    sngIntensitySorted[intIndex] = sngIonsIntensity[intIndex];
                    intPointerArray[intIndex] = intIndex;
                }

                // Sort by ascending intensity
                Array.Sort(sngIntensitySorted, intPointerArray);

                // Now process the data from the highest intensity to the lowest intensity
                // As each data point is processed, we will either: 
                //  a) set its intensity to the negative of the actual intensity to mark it as being processed
                //  b) set its intensity to Single.MinValue (-3.40282347E+38) if the point is to be removed
                //     because it is within sngMZResolution m/z units of a point with a higher intensity

                intPointerIndex = intIonCount - 1;

                while (intPointerIndex >= 0)
                {
                    intIndex = intPointerArray[intPointerIndex];

                    if (sngIonsIntensity[intIndex] > 0)
                    {
                        // This point has not yet been processed

                        // Examine adjacent data points to the left (lower m/z)
                        intIndexAdjacent = intIndex - 1;
                        while (intIndexAdjacent >= 0)
                        {
                            if (dblIonsMZ[intIndex] - dblIonsMZ[intIndexAdjacent] < sngMZResolution)
                            {
                                // Mark this data point for removal since it is too close to the point at intIndex
                                if (sngIonsIntensity[intIndexAdjacent] > 0)
                                {
                                    sngIonsIntensity[intIndexAdjacent] = float.MinValue;
                                }
                            }
                            else
                            {
                                break; // TODO: might not be correct. Was : Exit Do
                            }
                            intIndexAdjacent -= 1;
                        }

                        // Examine adjacent data points to the right (higher m/z)
                        intIndexAdjacent = intIndex + 1;
                        while (intIndexAdjacent < intIonCount)
                        {
                            if (dblIonsMZ[intIndexAdjacent] - dblIonsMZ[intIndex] < sngMZResolution)
                            {
                                // Mark this data point for removal since it is too close to the point at intIndex
                                if (sngIonsIntensity[intIndexAdjacent] > 0)
                                {
                                    sngIonsIntensity[intIndexAdjacent] = float.MinValue;
                                }
                            }
                            else
                            {
                                break; // TODO: might not be correct. Was : Exit Do
                            }
                            intIndexAdjacent += 1;
                        }

                        sngIonsIntensity[intIndex] = -sngIonsIntensity[intIndex];
                    }
                    intPointerIndex -= 1;
                }

                // Now consolidate the data by copying in place
                intIonCountNew = 0;
                for (intIndex = 0; intIndex <= intIonCount - 1; intIndex++)
                {
                    if (sngIonsIntensity[intIndex] > float.MinValue)
                    {
                        // Keep this point; need to flip the intensity back to being positive
                        dblIonsMZ[intIonCountNew] = dblIonsMZ[intIndex];
                        sngIonsIntensity[intIonCountNew] = -sngIonsIntensity[intIndex];
                        bytChargeFiltered[intIonCountNew] = bytChargeFiltered[intIndex];
                        intIonCountNew += 1;
                    }
                }
                intIonCount = intIonCountNew;

            }
            catch (Exception ex)
            {
                if (ErrorEvent != null)
                {
                    ErrorEvent("Error in clsLCMSDataPlotter.CentroidMSData: " + ex.Message);
                }
            }

        }


        private void DiscardDataToLimitIonCount(clsScanData objMSSpectrum, double dblMZIgnoreRangeStart, double dblMZIgnoreRangeEnd, int intMaxIonCountToRetain)
        {
            // When this is true, then will write a text file of the mass spectrum before before and after it is filtered
            // Used for debugging
            var blnWriteDebugData = false;
            StreamWriter swOutFile = null;

            try
            {
                var blnMZIgnoreRangleEnabled = false;
                if (dblMZIgnoreRangeStart > 0 | dblMZIgnoreRangeEnd > 0)
                {
                    blnMZIgnoreRangleEnabled = true;
                }
                else
                {
                    blnMZIgnoreRangleEnabled = false;
                }


                var intIonCountNew = 0;
                var intIonIndex = 0;
                if (objMSSpectrum.IonCount > intMaxIonCountToRetain)
                {
                    var objFilterDataArray = new clsFilterDataArrayMaxCount(objMSSpectrum.IonCount)
                    {
                        MaximumDataCountToLoad = intMaxIonCountToRetain,
                        TotalIntensityPercentageFilterEnabled = false
                    };


                    blnWriteDebugData = false;
                    if (blnWriteDebugData)
                    {
                        swOutFile = new StreamWriter(new FileStream("DataDump_" + objMSSpectrum.ScanNumber.ToString() + "_BeforeFilter.txt", FileMode.Create, FileAccess.Write, FileShare.Read));
                        swOutFile.WriteLine("m/z" + '\t' + "Intensity");
                    }

                    // Store the intensity values in objFilterDataArray
                    for (intIonIndex = 0; intIonIndex <= objMSSpectrum.IonCount - 1; intIonIndex++)
                    {
                        objFilterDataArray.AddDataPoint(objMSSpectrum.IonsIntensity[intIonIndex], intIonIndex);
                        if (blnWriteDebugData)
                        {
                            swOutFile.WriteLine(objMSSpectrum.IonsMZ[intIonIndex] + '\t' + objMSSpectrum.IonsIntensity[intIonIndex]);
                        }
                    }

                    if (blnWriteDebugData)
                    {
                        swOutFile.Close();
                    }


                    // Call .FilterData, which will determine which data points to keep
                    objFilterDataArray.FilterData();

                    intIonCountNew = 0;

                    for (intIonIndex = 0; intIonIndex <= objMSSpectrum.IonCount - 1; intIonIndex++)
                    {
                        var blnPointPassesFilter = false;
                        if (blnMZIgnoreRangleEnabled)
                        {
                            if (objMSSpectrum.IonsMZ[intIonIndex] <= dblMZIgnoreRangeEnd && objMSSpectrum.IonsMZ[intIonIndex] >= dblMZIgnoreRangeStart)
                            {
                                // The m/z value is between dblMZIgnoreRangeStart and dblMZIgnoreRangeEnd
                                // Keep this point
                                blnPointPassesFilter = true;
                            }
                            else
                            {
                                blnPointPassesFilter = false;
                            }
                        }
                        else
                        {
                            blnPointPassesFilter = false;
                        }

                        if (!blnPointPassesFilter)
                        {
                            // See if the point's intensity is negative
                            if (objFilterDataArray.GetAbundanceByIndex(intIonIndex) >= 0)
                            {
                                blnPointPassesFilter = true;
                            }
                        }

                        if (blnPointPassesFilter)
                        {
                            objMSSpectrum.IonsMZ[intIonCountNew] = objMSSpectrum.IonsMZ[intIonIndex];
                            objMSSpectrum.IonsIntensity[intIonCountNew] = objMSSpectrum.IonsIntensity[intIonIndex];
                            objMSSpectrum.Charge[intIonCountNew] = objMSSpectrum.Charge[intIonIndex];
                            intIonCountNew += 1;
                        }

                    }
                }
                else
                {
                    intIonCountNew = objMSSpectrum.IonCount;
                }

                if (intIonCountNew < objMSSpectrum.IonCount)
                {
                    objMSSpectrum.IonCount = intIonCountNew;
                }

                if (blnWriteDebugData)
                {
                    swOutFile = new StreamWriter(new FileStream("DataDump_" + objMSSpectrum.ScanNumber.ToString() + "_PostFilter.txt", FileMode.Create, FileAccess.Write, FileShare.Read));
                    swOutFile.WriteLine("m/z" + '\t' + "Intensity");

                    // Store the intensity values in objFilterDataArray
                    for (intIonIndex = 0; intIonIndex <= objMSSpectrum.IonCount - 1; intIonIndex++)
                    {
                        swOutFile.WriteLine(objMSSpectrum.IonsMZ[intIonIndex] + '\t' + objMSSpectrum.IonsIntensity[intIonIndex]);
                    }
                    swOutFile.Close();
                }

            }
            catch (Exception ex)
            {
                throw new Exception("Error in clsLCMSDataPlotter.DiscardDataToLimitIonCount: " + ex.Message, ex);
            }

        }

        /// <summary>
        /// Returns the file name of the recently saved file of the given type
        /// </summary>
        /// <param name="eFileType">File type to find</param>
        /// <returns>File name if found; empty string if this file type was not saved</returns>
        /// <remarks>The list of recent files gets cleared each time you call Save2DPlots() or Reset()</remarks>
        public string GetRecentFileInfo(eOutputFileTypes eFileType)
        {
            int intIndex = 0;
            for (intIndex = 0; intIndex <= mRecentFiles.Count - 1; intIndex++)
            {
                if (mRecentFiles[intIndex].FileType == eFileType)
                {
                    return mRecentFiles[intIndex].FileName;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Returns the file name and path of the recently saved file of the given type
        /// </summary>
        /// <param name="eFileType">File type to find</param>
        /// <param name="strFileName">File name (output)</param>
        /// <param name="strFilePath">File Path (output)</param>
        /// <returns>True if a match was found; otherwise returns false</returns>
        /// <remarks>The list of recent files gets cleared each time you call Save2DPlots() or Reset()</remarks>
        public bool GetRecentFileInfo(eOutputFileTypes eFileType, ref string strFileName, ref string strFilePath)
        {
            int intIndex = 0;
            for (intIndex = 0; intIndex <= mRecentFiles.Count - 1; intIndex++)
            {
                if (mRecentFiles[intIndex].FileType == eFileType)
                {
                    strFileName = mRecentFiles[intIndex].FileName;
                    strFilePath = mRecentFiles[intIndex].FilePath;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the cached scan data for the scan index
        /// </summary>
        /// <param name="intIndex"></param>
        /// <returns>ScanData class</returns>
        /// <remarks></remarks>
        public clsScanData GetCachedScanByIndex(int intIndex)
        {

            if (intIndex >= 0 && intIndex < mScans.Count)
            {
                return mScans[intIndex];
            }
            else
            {
                return null;
            }

        }


        public void Reset()
        {
            mPointCountCached = 0;
            mPointCountCachedAfterLastTrim = 0;

            if (mScans == null)
            {
                mScans = new List<clsLCMSDataPlotter.clsScanData>();
            }
            else
            {
                mScans.Clear();
            }

            ClearRecentFileInfo();
        }
        readonly Microsoft.VisualBasic.CompilerServices.StaticLocalInitFlag static_TrimCachedData_dtLastGCTime_Init = new Microsoft.VisualBasic.CompilerServices.StaticLocalInitFlag();

        /// <summary>
        /// Filters the data stored in mScans to nominally retain the top intTargetDataPointCount data points, sorted by descending intensity
        /// </summary>
        /// <param name="intTargetDataPointCount">Target max number of data points (see remarks for caveat)</param>
        /// <remarks>Note that the number of data points remaining after calling this function may still be
        ///          more than intTargetDataPointCount, depending on intMinPointsPerSpectrum 
        /// For example, if intMinPointsPerSpectrum = 5 and we have 5000 scans, then there will be
        ///   at least 5*5000 = 25000 data points in memory.  If intTargetDataPointCount = 10000, then 
        ///   there could be as many as 25000 + 10000 = 25000 points in memory
        ///</remarks>

        DateTime static_TrimCachedData_dtLastGCTime;
        private void TrimCachedData(int intTargetDataPointCount, int intMinPointsPerSpectrum)
        {
            lock (static_TrimCachedData_dtLastGCTime_Init)
            {
                try
                {
                    if (InitStaticVariableHelper(static_TrimCachedData_dtLastGCTime_Init))
                    {
                        static_TrimCachedData_dtLastGCTime = DateTime.UtcNow;
                    }
                }
                finally
                {
                    static_TrimCachedData_dtLastGCTime_Init.State = 1;
                }
            }


            try
            {
                var objFilterDataArray = new clsFilterDataArrayMaxCount();

                objFilterDataArray.MaximumDataCountToLoad = intTargetDataPointCount;
                objFilterDataArray.TotalIntensityPercentageFilterEnabled = false;

                // Store the intensity values for each scan in objFilterDataArray
                // However, skip scans for which there are <= intMinPointsPerSpectrum data points

                var intMasterIonIndex = 0;
                var intScanIndex = 0;
                var intIonIndex = 0;
                for (intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++)
                {
                    if (mScans[intScanIndex].IonCount > intMinPointsPerSpectrum)
                    {
                        // Store the intensity values in objFilterDataArray
                        for (intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++)
                        {
                            objFilterDataArray.AddDataPoint(mScans[intScanIndex].IonsIntensity[intIonIndex], intMasterIonIndex);
                            intMasterIonIndex += 1;
                        }
                    }
                }

                // Call .FilterData, which will determine which data points to keep
                objFilterDataArray.FilterData();

                // Step through the scans and trim the data as needed
                intMasterIonIndex = 0;
                mPointCountCached = 0;


                for (intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++)
                {
                    if (mScans[intScanIndex].IonCount <= intMinPointsPerSpectrum)
                    {
                        // Skip this can since it has too few points
                        // No need to update intMasterIonIndex since it was skipped above when calling objFilterDataArray.AddDataPoint

                    }
                    else
                    {
                        // See if fewer than intMinPointsPerSpectrum points will remain after filtering
                        // If so, we'll need to handle this scan differently

                        var intMasterIonIndexStart = intMasterIonIndex;

                        var intIonCountNew = 0;
                        for (intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++)
                        {
                            // If the point's intensity is >= 0, then we keep it
                            if (objFilterDataArray.GetAbundanceByIndex(intMasterIonIndex) >= 0)
                            {
                                intIonCountNew += 1;
                            }
                            intMasterIonIndex += 1;
                        }

                        if (intIonCountNew < intMinPointsPerSpectrum)
                        {
                            // Too few points will remain after filtering
                            // Retain the top intMinPointsPerSpectrum points in this spectrum

                            DiscardDataToLimitIonCount(mScans[intScanIndex], 0, 0, intMinPointsPerSpectrum);

                        }
                        else
                        {
                            // It's safe to filter the data


                            // Reset intMasterIonIndex to the saved value
                            intMasterIonIndex = intMasterIonIndexStart;

                            intIonCountNew = 0;

                            for (intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++)
                            {
                                // If the point's intensity is >= 0, then we keep it

                                if (objFilterDataArray.GetAbundanceByIndex(intMasterIonIndex) >= 0)
                                {
                                    // Copying in place (don't actually need to copy unless intIonCountNew <> intIonIndex)
                                    if (intIonCountNew != intIonIndex)
                                    {
                                        mScans[intScanIndex].IonsMZ[intIonCountNew] = mScans[intScanIndex].IonsMZ[intIonIndex];
                                        mScans[intScanIndex].IonsIntensity[intIonCountNew] = mScans[intScanIndex].IonsIntensity[intIonIndex];
                                        mScans[intScanIndex].Charge[intIonCountNew] = mScans[intScanIndex].Charge[intIonIndex];
                                    }

                                    intIonCountNew += 1;
                                }

                                intMasterIonIndex += 1;
                            }

                            mScans[intScanIndex].IonCount = intIonCountNew;

                        }



                        if (mScans[intScanIndex].IonsMZ.Length > 5 && mScans[intScanIndex].IonCount < mScans[intScanIndex].IonsMZ.Length / 2.0)
                        {
                            // Shrink the arrays to reduce the memory footprint
                            mScans[intScanIndex].ShrinkArrays();

                            if (DateTime.UtcNow.Subtract(static_TrimCachedData_dtLastGCTime).TotalSeconds > 60)
                            {
                                // Perform garbage collection every 60 seconds
                                static_TrimCachedData_dtLastGCTime = DateTime.UtcNow;
                                PRISM.Processes.clsProgRunner.GarbageCollectNow();
                            }

                        }

                    }

                    // Bump up the total point count cached
                    mPointCountCached += mScans[intScanIndex].IonCount;

                }

                // Update mPointCountCachedAfterLastTrim
                mPointCountCachedAfterLastTrim = mPointCountCached;

            }
            catch (Exception ex)
            {
                throw new Exception("Error in clsLCMSDataPlotter.TrimCachedData: " + ex.Message, ex);
            }

        }

        private void UpdateMinMax(float sngValue, ref float sngMin, ref float sngMax)
        {
            if (sngValue < sngMin)
            {
                sngMin = sngValue;
            }

            if (sngValue > sngMax)
            {
                sngMax = sngValue;
            }
        }

        private void UpdateMinMax(double dblValue, ref double dblMin, ref double dblMax)
        {
            if (dblValue < dblMin)
            {
                dblMin = dblValue;
            }

            if (dblValue > dblMax)
            {
                dblMax = dblValue;
            }
        }

        private void ValidateMSLevel()
        {
            int intIndex = 0;
            bool blnMSLevelDefined = false;

            for (intIndex = 0; intIndex <= mScans.Count - 1; intIndex++)
            {
                if (mScans[intIndex].MSLevel > 0)
                {
                    blnMSLevelDefined = true;
                    break; // TODO: might not be correct. Was : Exit For
                }
            }

            if (!blnMSLevelDefined)
            {
                // Set the MSLevel to 1 for all scans
                for (intIndex = 0; intIndex <= mScans.Count - 1; intIndex++)
                {
                    mScans[intIndex].UpdateMSLevel(1);
                }
            }

        }

        #region "Plotting Functions"


        private void AddSeriesMonoMassVsScan(IList<List<ScatterPoint>> lstPointsByCharge, PlotModel myPlot)
        {
            // Determine the number of data points to be plotted
            object intTotalPoints = 0;
            for (intCharge = 0; intCharge <= lstPointsByCharge.Count - 1; intCharge++)
            {
                intTotalPoints += lstPointsByCharge(intCharge).Count;
            }


            for (intCharge = 0; intCharge <= lstPointsByCharge.Count - 1; intCharge++)
            {
                if (lstPointsByCharge(intCharge).Count == 0)
                    continue;

                object strTitle = intCharge + "+";

                Color seriesColor = clsPlotContainer.GetColorByCharge(intCharge);

                object series = new ScatterSeries();

                // series.MarkerStroke = OxyColor.FromArgb(seriesColor.A, seriesColor.R, seriesColor.G, seriesColor.B)
                series.MarkerType = MarkerType.Circle;
                series.MarkerFill = OxyColor.FromArgb(seriesColor.A, seriesColor.R, seriesColor.G, seriesColor.B);
                series.Title = strTitle;

                // Customize the points
                if (mScans.Count < 250)
                {
                    // Use a point size of 2 when fewer than 250 scans
                    series.MarkerSize = 2;
                }
                else if (mScans.Count < 500)
                {
                    // Use a point size of 1 when 250 to 500 scans
                    series.MarkerSize = 1;
                }
                else
                {
                    // Use a point size of 0.8 or 0.6 when >= 500 scans
                    if (intTotalPoints < 80000)
                    {
                        series.MarkerSize = 0.8;
                    }
                    else
                    {
                        series.MarkerSize = 0.6;
                    }
                }

                series.Points.AddRange(lstPointsByCharge(intCharge));

                myPlot.Series.Add(series);
            }

        }


        private void AddSeriesMzVsScan(string strTitle, IEnumerable<ScatterPoint> objPoints, float sngColorScaleMinIntensity, float sngColorScaleMaxIntensity, PlotModel myPlot)
        {
            // We use a linear color axis to color the data points based on intensity
            object colorAxis = new LinearColorAxis
            {
                Position = AxisPosition.Right,
                Minimum = sngColorScaleMinIntensity,
                Maximum = sngColorScaleMaxIntensity,
                Palette = OxyPalettes.Jet(30),
                IsAxisVisible = false
            };

            myPlot.Axes.Add(colorAxis);

            object series = new ScatterSeries();

            series.MarkerType = MarkerType.Circle;
            series.Title = strTitle;

            // Customize the point size
            if (mScans.Count < 250)
            {
                // Use a point size of 2 when fewer than 250 scans
                series.MarkerSize = 2;
            }
            else if (mScans.Count < 5000)
            {
                // Use a point size of 1 when 250 to 5000 scans
                series.MarkerSize = 1;
            }
            else
            {
                // Use a point size of 0.6 when >= 5000 scans
                series.MarkerSize = 0.6;
            }

            series.Points.AddRange(objPoints);

            myPlot.Series.Add(series);
        }

        private float ComputeMedian(ref float[] sngList, int intItemCount)
        {
            var blnAverage = false;

            if (sngList == null || sngList.Length < 1 || intItemCount < 1)
            {
                // List is empty (or intItemCount = 0)
                return 0;
            }
            
            if (intItemCount <= 1)
            {
                // Only 1 item; the median is the value
                return sngList[0];
            }

            // Sort sngList ascending, then find the midpoint
            Array.Sort(sngList, 0, intItemCount);

            var intMidpointIndex = 0;
            if (intItemCount % 2 == 0)
            {
                // Even number
                intMidpointIndex = Convert.ToInt32(Math.Floor(intItemCount / 2.0)) - 1;
                blnAverage = true;
            }
            else
            {
                // Odd number
                intMidpointIndex = Convert.ToInt32(Math.Floor(intItemCount / 2.0));
            }

            if (intMidpointIndex > intItemCount)
                intMidpointIndex = intItemCount - 1;
            if (intMidpointIndex < 0)
                intMidpointIndex = 0;

            if (blnAverage)
            {
                // Even number of items
                // Return the average of the two middle points
                return (sngList[intMidpointIndex] + sngList[intMidpointIndex + 1]) / 2;
            }

            // Odd number of items
            return sngList[intMidpointIndex];
        }

        private List<List<ScatterPoint>> GetMonoMassSeriesByCharge(int intMsLevelFilter, ref double dblMinMz, ref double dblMaxMz, ref double dblScanTimeMax, ref int intMinScan, ref int intMaxScan)
        {

            double dblScanTimeMin = 0;

            intMinScan = int.MaxValue;
            intMaxScan = 0;
            dblMinMz = float.MaxValue;
            dblMaxMz = 0;

            dblScanTimeMin = double.MaxValue;
            dblScanTimeMax = 0;

            // Determine the maximum charge state
            byte intMaxCharge = 1;

            for (var intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++) {
                if (intMsLevelFilter == 0 || mScans[intScanIndex].MSLevel == intMsLevelFilter) {
                    if (mScans[intScanIndex].Charge.Length > 0) {
                        for (var intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++) {
                            intMaxCharge = Math.Max(intMaxCharge, mScans[intScanIndex].Charge[intIonIndex]);
                        }
                    }
                }
            }

            // Initialize the data for each charge state
            var lstSeries = new List<List<ScatterPoint>>();

            for (var intCharge = 0; intCharge <= intMaxCharge; intCharge++) {
                lstSeries.Add(new List<ScatterPoint>());
            }

            // Store the data, segregating by charge
            for (var intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++) {

                if (intMsLevelFilter == 0 || mScans[intScanIndex].MSLevel == intMsLevelFilter) {
                    for (var intIonIndex = 0; intIonIndex <= mScans[intScanIndex].IonCount - 1; intIonIndex++) {
                        var dataPoint = new ScatterPoint(mScans[intScanIndex].ScanNumber,
                                                         mScans[intScanIndex].IonsMZ[intIonIndex])
                        {
                            Value = mScans[intScanIndex].IonsIntensity[intIonIndex]
                        };

                        lstSeries[mScans[intScanIndex].Charge[intIonIndex]].Add(dataPoint);

                        UpdateMinMax(mScans[intScanIndex].IonsMZ[intIonIndex], ref dblMinMz, ref dblMaxMz);

                    }

                    UpdateMinMax(mScans[intScanIndex].ScanTimeMinutes, ref dblScanTimeMin, ref dblScanTimeMax);

                    if (mScans[intScanIndex].ScanNumber < intMinScan)
                    {
                        intMinScan = mScans[intScanIndex].ScanNumber;
                    }

                    if (mScans[intScanIndex].ScanNumber > intMaxScan)
                    {
                        intMaxScan = mScans[intScanIndex].ScanNumber;
                    }
                }

            }

            return lstSeries;

        }

        private List<ScatterPoint> GetMzVsScanSeries(int intMSLevelFilter, ref float sngColorScaleMinIntensity, ref float sngColorScaleMaxIntensity, ref double dblMinMZ, ref double dblMaxMZ, ref double dblScanTimeMax, ref int intMinScan, ref int intMaxScan, bool blnWriteDebugData, StreamWriter swDebugFile)
        {

            int intScanIndex = 0;

            int intSortedIntensityListCount = 0;
            float[] sngSortedIntensityList = null;

            double dblIntensitySum = 0;
            float sngAvgIntensity = 0;

            double dblScanTimeMin = 0;

            var objPoints = new List<ScatterPoint>();

            dblIntensitySum = 0;
            intSortedIntensityListCount = 0;
            sngSortedIntensityList = new float[mPointCountCached + 1];

            sngColorScaleMinIntensity = float.MaxValue;
            sngColorScaleMaxIntensity = 0;

            intMinScan = int.MaxValue;
            intMaxScan = 0;
            dblMinMZ = float.MaxValue;
            dblMaxMZ = 0;

            dblScanTimeMin = double.MaxValue;
            dblScanTimeMax = 0;

            for (intScanIndex = 0; intScanIndex <= mScans.Count - 1; intScanIndex++) {

                if (intMSLevelFilter == 0 || mScans[intScanIndex].MSLevel == intMSLevelFilter) {
                    var intIonIndex = 0;
                    for (intIonIndex = 0; intIonIndex <=  mScans[intScanIndex].IonCount - 1; intIonIndex++) {
                        if (intSortedIntensityListCount >= sngSortedIntensityList.Length) {
                            // Need to reserve more room (this is unexpected)
                            Array.Resize(ref sngSortedIntensityList, sngSortedIntensityList.Length * 2);
                        }

                        sngSortedIntensityList[intSortedIntensityListCount] =  mScans[intScanIndex].IonsIntensity[intIonIndex];
                        dblIntensitySum += sngSortedIntensityList[intSortedIntensityListCount);

                        var dataPoint = new ScatterPoint(mScans[intScanIndex].ScanNumber,
                                                         mScans[intScanIndex].IonsMZ[intIonIndex])
                        {
                            Value = mScans[intScanIndex].IonsIntensity[intIonIndex]
                        };

                        objPoints.Add(dataPoint);

                        if (blnWriteDebugData) {
                            swDebugFile.WriteLine( mScans[intScanIndex].ScanNumber + '\t' +  mScans[intScanIndex].IonsMZ[intIonIndex] + '\t' +  mScans[intScanIndex].IonsIntensity[intIonIndex]);
                        }

                        UpdateMinMax(sngSortedIntensityList[intSortedIntensityListCount], ref sngColorScaleMinIntensity, ref sngColorScaleMaxIntensity);
                        UpdateMinMax( mScans[intScanIndex].IonsMZ[intIonIndex], ref dblMinMZ, ref dblMaxMZ);

                        intSortedIntensityListCount += 1;
                    }

                    UpdateMinMax( mScans[intScanIndex].ScanTimeMinutes, ref dblScanTimeMin, ref dblScanTimeMax);

                    if ( mScans[intScanIndex].ScanNumber < intMinScan) {
                        intMinScan =  mScans[intScanIndex].ScanNumber;
                    }

                    if ( mScans[intScanIndex].ScanNumber > intMaxScan) {
                        intMaxScan =  mScans[intScanIndex].ScanNumber;
                    }
                }

            }


            if (objPoints.Count > 0) {
                // Compute median and average intensity values
                if (intSortedIntensityListCount > 0) {
                    Array.Sort(sngSortedIntensityList, 0, intSortedIntensityListCount);
                    var sngMedianIntensity = ComputeMedian(ref sngSortedIntensityList, intSortedIntensityListCount);
                    sngAvgIntensity = Convert.ToSingle(dblIntensitySum / intSortedIntensityListCount);

                    // Set the minimum color intensity to the median
                    sngColorScaleMinIntensity = sngMedianIntensity;
                }

            }

            return objPoints;
        }

        /// <summary>
        /// When PlottingDeisotopedData is False, creates a 2D plot of m/z vs. scan number, using Intensity as the 3rd dimension to color the data points
        /// When PlottingDeisotopedData is True, creates a 2D plot of monoisotopic mass vs. scan number, using charge state as the 3rd dimension to color the data points
        /// </summary>
        /// <param name="strTitle">Title of the plot</param>
        /// <param name="intMSLevelFilter">0 to use all of the data, 1 to use data from MS scans, 2 to use data from MS2 scans, etc.</param>
        /// <param name="blnSkipTrimCachedData">When True, then doesn't call TrimCachedData (when making several plots in success, each with a different value for intMSLevelFilter, set blnSkipTrimCachedData to False on the first call and True on subsequent calls)</param>
        /// <returns>OxyPlot PlotContainer</returns>
        /// <remarks></remarks>
        private clsPlotContainer InitializePlot(string strTitle, int intMSLevelFilter, bool blnSkipTrimCachedData)
        {

            int intMinScan = 0;
            int intMaxScan = 0;

            float sngColorScaleMinIntensity = 0;
            float sngColorScaleMaxIntensity = 0;

            double dblMinMZ = 0;
            double dblMaxMZ = 0;

            double dblScanTimeMax = 0;

            if (!blnSkipTrimCachedData && mPointCountCached > mOptions.MaxPointsToPlot) {
                // Need to step through the scans and reduce the number of points in memory

                // Note that the number of data points remaining after calling this function may still be
                //  more than mOptions.MaxPointsToPlot, depending on mOptions.MinPointsPerSpectrum 
                //  (see TrimCachedData for more details)

                TrimCachedData(mOptions.MaxPointsToPlot, mOptions.MinPointsPerSpectrum);

            }

            // When this is true, then will write a text file of the mass spectrum before before and after it is filtered
            // Used for debugging
            bool blnWriteDebugData = false;
            StreamWriter swDebugFile = null;

            blnWriteDebugData = false;
            if (blnWriteDebugData) {
                swDebugFile = new StreamWriter(new FileStream(strTitle + " - LCMS Top " + IntToEngineeringNotation(mOptions.MaxPointsToPlot) + " points.txt", FileMode.Create, FileAccess.Write, FileShare.Read));
                swDebugFile.WriteLine("scan" + '\t' + "m/z" + '\t' + "Intensity");
            }

            // Populate objPoints and objScanTimePoints with the data
            // At the same time, determine the range of m/z and intensity values
            // Lastly, compute the median and average intensity values

            // Instantiate the list to track the data points
            object lstPointsByCharge = new List<List<ScatterPoint>>();

            if (mOptions.PlottingDeisotopedData) {
                lstPointsByCharge = GetMonoMassSeriesByCharge(intMSLevelFilter, ref dblMinMZ, ref dblMaxMZ, ref dblScanTimeMax, ref intMinScan, ref intMaxScan);
            } else {
                object objPoints = GetMzVsScanSeries(intMSLevelFilter, ref sngColorScaleMinIntensity, ref sngColorScaleMaxIntensity, ref dblMinMZ, ref dblMaxMZ, ref dblScanTimeMax, ref intMinScan, ref intMaxScan, blnWriteDebugData, swDebugFile);
                lstPointsByCharge.Add(objPoints);
            }

            if (blnWriteDebugData) {
                swDebugFile.Close();
            }

            object dblMaxMzToUse = double.MaxValue;
            if (mOptions.PlottingDeisotopedData) {
                dblMaxMzToUse = mOptions.MaxMonoMassForDeisotopedPlot;
            }

            // Count the actual number of points that will be plotted
            int intPointsToPlot = 0;
            foreach (void objSeries_loopVariable in lstPointsByCharge) {
                objSeries = objSeries_loopVariable;
                foreach (void item_loopVariable in objSeries) {
                    item = item_loopVariable;
                    if (item.Y < dblMaxMzToUse) {
                        intPointsToPlot += 1;
                    }
                }
            }

            if (intPointsToPlot == 0) {
                // Nothing to plot
                return new clsPlotContainer(new PlotModel());
            }

            // Round intMinScan down to the nearest multiple of 10
            intMinScan = Convert.ToInt32(Math.Floor(intMinScan / 10.0) * 10);
            if (intMinScan < 0)
                intMinScan = 0;

            // Round intMaxScan up to the nearest multiple of 10
            intMaxScan = Convert.ToInt32(Math.Ceiling(intMaxScan / 10.0) * 10);

            // Round dblMinMZ down to the nearest multiple of 100
            dblMinMZ = Convert.ToInt64(Math.Floor(dblMinMZ / 100.0) * 100);

            // Round dblMaxMZ up to the nearest multiple of 100
            dblMaxMZ = Convert.ToInt64(Math.Ceiling(dblMaxMZ / 100.0) * 100);

            string yAxisLabel = null;
            if (mOptions.PlottingDeisotopedData) {
                yAxisLabel = "Monoisotopic Mass";
            } else {
                yAxisLabel = "m/z";
            }

            object myPlot = clsOxyplotUtilities.GetBasicPlotModel(strTitle, "LC Scan Number", yAxisLabel);

            if (mOptions.PlottingDeisotopedData) {
                AddSeriesMonoMassVsScan(lstPointsByCharge, myPlot);
                myPlot.TitlePadding = 40;
            } else {
                AddSeriesMzVsScan(strTitle, lstPointsByCharge.First(), sngColorScaleMinIntensity, sngColorScaleMaxIntensity, myPlot);
            }

            // Update the axis format codes if the data values are small or the range of data is small
            object xVals = from item in lstPointsByCharge.First()item.X;
            clsOxyplotUtilities.UpdateAxisFormatCodeIfSmallValues(myPlot.Axes(0), xVals, true);

            object yVals = from item in lstPointsByCharge.First()item.Y;
            clsOxyplotUtilities.UpdateAxisFormatCodeIfSmallValues(myPlot.Axes(1), yVals, false);

            object plotContainer = new clsPlotContainer(myPlot);
            plotContainer.FontSizeBase = clsOxyplotUtilities.FONT_SIZE_BASE;

            // Add a label showing the number of points displayed
            plotContainer.AnnotationBottomLeft = intPointsToPlot.ToString("0,000") + " points plotted";

            // Possibly add a label showing the maximum elution time

            if (dblScanTimeMax > 0) {
                string strCaption = null;
                if (dblScanTimeMax < 2) {
                    strCaption = Math.Round(dblScanTimeMax, 2).ToString("0.00") + " minutes";
                } else if (dblScanTimeMax < 10) {
                    strCaption = Math.Round(dblScanTimeMax, 1).ToString("0.0") + " minutes";
                } else {
                    strCaption = Math.Round(dblScanTimeMax, 0).ToString("0") + " minutes";
                }

                plotContainer.AnnotationBottomRight = strCaption;

            }

            // Override the auto-computed X axis range
            if (mOptions.UseObservedMinScan) {
                myPlot.Axes(0).Minimum = intMinScan;
            } else {
                myPlot.Axes(0).Minimum = 0;
            }

            if (intMaxScan == 0) {
                myPlot.Axes(0).Maximum = 1;
            } else {
                myPlot.Axes(0).Maximum = intMaxScan;
            }

            if (Math.Abs(myPlot.Axes(0).Minimum - myPlot.Axes(0).Maximum) < 0.01) {
                intMinScan = Convert.ToInt32(myPlot.Axes(0).Minimum);
                myPlot.Axes(0).Minimum = intMinScan - 1;
                myPlot.Axes(0).Maximum = intMinScan + 1;
            } else if (intMinScan == intMaxScan) {
                myPlot.Axes(0).Minimum = intMinScan - 1;
                myPlot.Axes(0).Maximum = intMinScan + 1;
            }

            // Assure that we don't see ticks between scan numbers
            clsOxyplotUtilities.ValidateMajorStep(myPlot.Axes(0));

            // Set the maximum value for the Y-axis
            if (mOptions.PlottingDeisotopedData) {
                if (dblMaxMZ < mOptions.MaxMonoMassForDeisotopedPlot) {
                    dblMaxMzToUse = dblMaxMZ;
                } else {
                    dblMaxMzToUse = mOptions.MaxMonoMassForDeisotopedPlot;
                }
            } else {
                dblMaxMzToUse = dblMaxMZ;
            }

            // Override the auto-computed axis range
            myPlot.Axes(1).Minimum = dblMinMZ;
            myPlot.Axes(1).Maximum = dblMaxMzToUse;

            // Hide the legend
            myPlot.IsLegendVisible = false;

            return plotContainer;

        }

        /// <summary>
        /// Converts an integer to engineering notation
        /// For example, 50000 will be returned as 50K
        /// </summary>
        /// <param name="intValue"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        private string IntToEngineeringNotation(int intValue)
        {

            if (intValue < 1000)
            {
                return intValue.ToString;
            }
            else if (intValue < 1000000.0)
            {
                return Convert.ToInt32(Math.Round(intValue / 1000, 0)).ToString + "K";
            }
            else
            {
                return Convert.ToInt32(Math.Round(intValue / 1000 / 1000, 0)).ToString + "M";
            }

        }

        public bool Save2DPlots(string strDatasetName, string strOutputFolderPath)
        {

            return Save2DPlots(strDatasetName, strOutputFolderPath, "", "");

        }

        public bool Save2DPlots(string strDatasetName, string strOutputFolderPath, string strFileNameSuffixAddon, string strScanModeSuffixAddon)
        {

            const bool EMBED_FILTER_SETTINGS_IN_NAME = false;

            clsPlotContainer plotContainer = default(clsPlotContainer);
            string strPNGFilePath = null;
            bool blnSuccess = false;


            try
            {
                ClearRecentFileInfo();

                // Check whether all of the spectra have .MSLevel = 0
                // If they do, change the level to 1
                ValidateMSLevel();

                if (strFileNameSuffixAddon == null)
                    strFileNameSuffixAddon = string.Empty;
                if (strScanModeSuffixAddon == null)
                    strScanModeSuffixAddon = string.Empty;


                object colorGradients = new Dictionary<string, OxyPalette>();
                colorGradients.Add("BlackWhiteRed30", OxyPalettes.BlackWhiteRed(30));
                colorGradients.Add("BlueWhiteRed30", OxyPalettes.BlueWhiteRed(30));
                colorGradients.Add("Cool30", OxyPalettes.Cool(30));
                colorGradients.Add("Gray30", OxyPalettes.Gray(30));
                colorGradients.Add("Hot30", OxyPalettes.Hot(30));
                colorGradients.Add("Hue30", OxyPalettes.Hue(30));
                colorGradients.Add("HueDistinct30", OxyPalettes.HueDistinct(30));
                colorGradients.Add("Jet30", OxyPalettes.Jet(30));
                colorGradients.Add("Rainbow30", OxyPalettes.Rainbow(30));

                plotContainer = InitializePlot(strDatasetName + " - " + mOptions.MS1PlotTitle, 1, false);
                plotContainer.PlottingDeisotopedData = mOptions.PlottingDeisotopedData;

                if (mOptions.TestGradientColorSchemes)
                {
                    plotContainer.AddGradients(colorGradients);
                }

                if (plotContainer.SeriesCount > 0)
                {
                    if (EMBED_FILTER_SETTINGS_IN_NAME)
                    {
                        strPNGFilePath = strDatasetName + "_" + strFileNameSuffixAddon + "LCMS_" + mOptions.MaxPointsToPlot + "_" + mOptions.MinPointsPerSpectrum + "_" + mOptions.MZResolution.ToString("0.00") + strScanModeSuffixAddon + ".png";
                    }
                    else
                    {
                        strPNGFilePath = strDatasetName + "_" + strFileNameSuffixAddon + "LCMS" + strScanModeSuffixAddon + ".png";
                    }
                    strPNGFilePath = Path.Combine(strOutputFolderPath, strPNGFilePath);
                    plotContainer.SaveToPNG(strPNGFilePath, 1024, 700, 96);
                    AddRecentFile(strPNGFilePath, eOutputFileTypes.LCMS);
                }

                plotContainer = InitializePlot(strDatasetName + " - " + mOptions.MS2PlotTitle, 2, true);
                if (plotContainer.SeriesCount > 0)
                {
                    strPNGFilePath = Path.Combine(strOutputFolderPath, strDatasetName + "_" + strFileNameSuffixAddon + "LCMS_MSn" + strScanModeSuffixAddon + ".png");
                    plotContainer.SaveToPNG(strPNGFilePath, 1024, 700, 96);
                    AddRecentFile(strPNGFilePath, eOutputFileTypes.LCMSMSn);
                }

                blnSuccess = true;

            }
            catch (Exception ex)
            {
                if (ErrorEvent != null)
                {
                    ErrorEvent("Error in clsLCMSDataPlotter.Save2DPlots: " + ex.Message);
                }
                blnSuccess = false;
            }

            return blnSuccess;

        }

        #endregion

        /// <summary>
        /// This class tracks the m/z and intensity values for a given scan
        /// It can optionally also track charge state
        /// Be sure to use .IonCount to determine the number of data points, not .IonsMZ.Length
        /// If you decrease .IonCount, you can optionally call .ShrinkArrays to reduce the allocated space
        /// </summary>
        /// <remarks></remarks>
        public class clsScanData
        {

            private readonly int mScanNumber;
            private int mMSLevel;

            private readonly float mScanTimeMinutes;
            public int IonCount;
            public double[] IonsMZ;
            public float[] IonsIntensity;

            public byte[] Charge;
            public int MSLevel
            {
                get { return mMSLevel; }
            }

            public int ScanNumber
            {
                get { return mScanNumber; }
            }

            public float ScanTimeMinutes
            {
                get { return mScanTimeMinutes; }
            }


            public clsScanData(int intScanNumber, int intMSLevel, float sngScanTimeMinutes, int intDataCount, double[] dblIonsMZ, float[] sngIonsIntensity, byte[] bytCharge)
            {
                mScanNumber = intScanNumber;
                mMSLevel = intMSLevel;
                mScanTimeMinutes = sngScanTimeMinutes;

                IonCount = intDataCount;
                IonsMZ = new double[intDataCount];
                IonsIntensity = new float[intDataCount];
                Charge = new byte[intDataCount];

                // Populate the arrays to be filtered
                Array.Copy(dblIonsMZ, IonsMZ, intDataCount);
                Array.Copy(sngIonsIntensity, IonsIntensity, intDataCount);
                Array.Copy(bytCharge, Charge, intDataCount);
            }

            public void ShrinkArrays()
            {
                if (IonCount < IonsMZ.Length)
                {
                    Array.Resize(ref IonsMZ, IonCount);
                    Array.Resize(ref IonsIntensity, IonCount);
                    Array.Resize(ref Charge, IonCount);
                }
            }

            public void UpdateMSLevel(int NewMSLevel)
            {
                mMSLevel = NewMSLevel;
            }

        }

        public class udtMSIonTypeComparer : IComparer<udtMSIonType>
        {

            public int Compare(udtMSIonType x, udtMSIonType y)
            {
                return x.MZ.CompareTo(y.MZ);
            }
        }
        static bool InitStaticVariableHelper(Microsoft.VisualBasic.CompilerServices.StaticLocalInitFlag flag)
        {
            if (flag.State == 0)
            {
                flag.State = 2;
                return true;
            }
            else if (flag.State == 2)
            {
                throw new Microsoft.VisualBasic.CompilerServices.IncompleteInitialization();
            }
            else
            {
                return false;
            }
        }

    }
}