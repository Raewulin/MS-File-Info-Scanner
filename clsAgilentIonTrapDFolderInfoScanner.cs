using System;
using System.IO;
using MSFileInfoScanner.DatasetStats;

// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
// Started in 2005
//

namespace MSFileInfoScanner
{
    public class clsAgilentIonTrapDFolderInfoScanner : clsMSFileInfoProcessorBaseClass
    {

        // Note: The extension must be in all caps

        public const string AGILENT_ION_TRAP_D_EXTENSION = ".D";
        private const string AGILENT_YEP_FILE = "Analysis.yep";
        private const string AGILENT_RUN_LOG_FILE = "RUN.LOG";

        private const string AGILENT_ANALYSIS_CDF_FILE = "Analysis.cdf";
        private const string RUN_LOG_FILE_METHOD_LINE_START = "Method";
        private const string RUN_LOG_FILE_INSTRUMENT_RUNNING = "Instrument running sample";

        private const string RUN_LOG_INSTRUMENT_RUN_COMPLETED = "Instrument run completed";

        private bool ExtractMethodLineDate(string dataLine, out DateTime methodDate)
        {

            var success = false;
            methodDate = DateTime.MinValue;

            try
            {
                var splitLine = dataLine.Trim().Split(' ');
                if (splitLine.Length >= 2)
                {
                    success = DateTime.TryParse(splitLine[splitLine.Length - 1] + " " + splitLine[splitLine.Length - 2], out methodDate);
                }
            }
            catch (Exception)
            {
                // Ignore errors
            }

            return success;
        }

        public override string GetDatasetNameViaPath(string dataFilePath)
        {
            // The dataset name is simply the directory name without .D
            try
            {
                return Path.GetFileNameWithoutExtension(dataFilePath);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private TimeSpan SecondsToTimeSpan(double seconds)
        {

            TimeSpan timeSpanItem;

            try
            {
                timeSpanItem = new TimeSpan(0, 0, (int)seconds);
            }
            catch (Exception)
            {
                timeSpanItem = new TimeSpan(0, 0, 0);
            }

            return timeSpanItem;

        }

        private void ParseRunLogFile(string directoryPath, DatasetFileInfo datasetFileInfo)
        {
            var mostRecentMethodLine = string.Empty;

            try
            {
                // Try to open the Run.Log file
                bool processedFirstMethodLine;
                bool endDateFound;
                DateTime methodDate;

                using (var reader = new StreamReader(Path.Combine(directoryPath, AGILENT_RUN_LOG_FILE)))
                {

                    processedFirstMethodLine = false;
                    endDateFound = false;
                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();

                        if (string.IsNullOrWhiteSpace(dataLine))
                            continue;

                        if (!dataLine.StartsWith(RUN_LOG_FILE_METHOD_LINE_START))
                            continue;

                        mostRecentMethodLine = string.Copy(dataLine);

                        // Method line found
                        // See if the line contains a key phrase
                        var charIndex = dataLine.IndexOf(RUN_LOG_FILE_INSTRUMENT_RUNNING, StringComparison.Ordinal);
                        if (charIndex > 0)
                        {
                            if (ExtractMethodLineDate(dataLine, out methodDate))
                            {
                                datasetFileInfo.AcqTimeStart = methodDate;
                            }
                            processedFirstMethodLine = true;
                        }
                        else
                        {
                            charIndex = dataLine.IndexOf(RUN_LOG_INSTRUMENT_RUN_COMPLETED, StringComparison.Ordinal);
                            if (charIndex > 0)
                            {
                                if (ExtractMethodLineDate(dataLine, out methodDate))
                                {
                                    datasetFileInfo.AcqTimeEnd = methodDate;
                                    endDateFound = true;
                                }
                            }
                        }

                        // If this is the first method line, then parse out the date and store in .AcqTimeStart
                        if (!processedFirstMethodLine)
                        {
                            if (ExtractMethodLineDate(dataLine, out methodDate))
                            {
                                datasetFileInfo.AcqTimeStart = methodDate;
                            }
                        }
                    }
                }

                if (processedFirstMethodLine && !endDateFound)
                {
                    // Use the last time in the file as the .AcqTimeEnd value
                    if (ExtractMethodLineDate(mostRecentMethodLine, out methodDate))
                    {
                        datasetFileInfo.AcqTimeEnd = methodDate;
                    }
                }

            }
            catch (Exception ex)
            {
                // Run.log file not found
                OnWarningEvent("Error in ParseRunLogFile: " + ex.Message);
            }

        }

        private void ParseAnalysisCDFFile(string directoryPath, DatasetFileInfo datasetFileInfo)
        {
            NetCDFReader.clsMSNetCdf netCDFReader = null;

            try
            {
                // Note: as of May 2016 this only works if you compile as x86 or if you enable "Prefer 32-bit" when compiling as AnyCPU
                // In contrast, XRawFileIO in clsFinniganRawFileInfoScanner requires that "Prefer 32-bit" be disabled

                netCDFReader = new NetCDFReader.clsMSNetCdf();
                var success = netCDFReader.OpenMSCdfFile(Path.Combine(directoryPath, AGILENT_ANALYSIS_CDF_FILE));
                if (success)
                {
                    var scanCount = netCDFReader.GetScanCount();

                    if (scanCount > 0)
                    {
                        // Lookup the scan time of the final scan

                        if (netCDFReader.GetScanInfo(scanCount - 1, out var scanNumber,
                            out _, out var scanTime, out _, out _))
                        {
                            // Add 1 to scanNumber since the scan number is off by one in the CDF file
                            datasetFileInfo.ScanCount = scanNumber + 1;
                            datasetFileInfo.AcqTimeEnd = datasetFileInfo.AcqTimeStart.Add(SecondsToTimeSpan(scanTime));
                        }
                    }
                    else
                    {
                        datasetFileInfo.ScanCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                OnWarningEvent("Error in ParseAnalysisCDFFile: " + ex.Message);
            }
            finally
            {
                netCDFReader?.CloseMSCdfFile();
            }

        }

        /// <summary>
        /// Process the data file
        /// </summary>
        /// <param name="dataFilePath">Dataset directory ptah</param>
        /// <param name="datasetFileInfo"></param>
        /// <returns>True if success, False if an error</returns>
        public override bool ProcessDataFile(string dataFilePath, DatasetFileInfo datasetFileInfo)
        {
            var success = false;

            ResetResults();

            try
            {
                var datasetDirectory = new DirectoryInfo(dataFilePath);

                datasetFileInfo.FileSystemCreationTime = datasetDirectory.CreationTime;
                datasetFileInfo.FileSystemModificationTime = datasetDirectory.LastWriteTime;

                // The acquisition times will get updated below to more accurate values
                datasetFileInfo.AcqTimeStart = datasetFileInfo.FileSystemModificationTime;
                datasetFileInfo.AcqTimeEnd = datasetFileInfo.FileSystemModificationTime;

                datasetFileInfo.DatasetName = GetDatasetNameViaPath(datasetDirectory.Name);
                datasetFileInfo.FileExtension = datasetDirectory.Extension;

                // Look for the Analysis.yep file
                // Use its modification time to get an initial estimate for the acquisition time
                // Assign the .Yep file's size to .FileSizeBytes
                var yepFile = new FileInfo(Path.Combine(datasetDirectory.FullName, AGILENT_YEP_FILE));
                if (yepFile.Exists)
                {
                    datasetFileInfo.FileSizeBytes = yepFile.Length;
                    datasetFileInfo.AcqTimeStart = yepFile.LastWriteTime;
                    datasetFileInfo.AcqTimeEnd = yepFile.LastWriteTime;

                    if (mDisableInstrumentHash)
                    {
                        mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFileNoHash(yepFile);
                    }
                    else
                    {
                        mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFile(yepFile);
                    }

                    success = true;
                }
                else
                {
                    // Analysis.yep not found; look for Run.log
                    var runLog = new FileInfo(Path.Combine(datasetDirectory.FullName, AGILENT_RUN_LOG_FILE));
                    if (runLog.Exists)
                    {
                        datasetFileInfo.AcqTimeStart = runLog.LastWriteTime;
                        datasetFileInfo.AcqTimeEnd = runLog.LastWriteTime;
                        success = true;

                        // Sum up the sizes of all of the files in this directory
                        datasetFileInfo.FileSizeBytes = 0;
                        foreach (var datasetFile in datasetDirectory.GetFiles())
                        {
                            datasetFileInfo.FileSizeBytes += datasetFile.Length;
                        }

                        // Compute the SHA-1 hash of the largest file in instrument directory
                        AddLargestInstrumentFile(datasetDirectory);
                    }
                }

                datasetFileInfo.ScanCount = 0;

                if (success)
                {
                    try
                    {
                        // Parse the Run Log file to determine the actual values for .AcqTimeStart and .AcqTimeEnd
                        ParseRunLogFile(dataFilePath, datasetFileInfo);

                        // Parse the Analysis.cdf file to determine the scan count and to further refine .AcqTimeStart
                        ParseAnalysisCDFFile(dataFilePath, datasetFileInfo);
                    }
                    catch (Exception)
                    {
                        // Error parsing the Run Log file or the Analysis.cdf file; do not abort
                    }
                }

                // Copy over the updated file time info from datasetFileInfo to mDatasetStatsSummarizer.DatasetFileInfo
                UpdateDatasetStatsSummarizerUsingDatasetFileInfo(datasetFileInfo);

                PostProcessTasks();

            }
            catch (Exception)
            {
                success = false;
            }

            PostProcessTasks();

            return success;
        }

    }
}
