using System;
using System.IO;

// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
// Started in 2005
//
// Last modified September 17, 2005

namespace MSFileInfoScanner
{
    public class clsMicromassRawFolderInfoScanner : clsMSFileInfoProcessorBaseClass
    {

        // Note: The extension must be in all caps

        public const string MICROMASS_RAW_FOLDER_EXTENSION = ".RAW";

        private readonly DateTime MINIMUM_ACCEPTABLE_ACQ_START_TIME = new DateTime(1975, 1, 1);

        public override string GetDatasetNameViaPath(string strDataFilePath)
        {
            // The dataset name is simply the folder name without .Raw
            try {
                return Path.GetFileNameWithoutExtension(strDataFilePath);
            } catch (Exception ex) {
                return string.Empty;
            }
        }

        private TimeSpan MinutesToTimeSpan(double dblMinutes)
        {

            int intMinutes = 0;
            int intSeconds = 0;
            TimeSpan dtTimeSpan = default(TimeSpan);

            try {
                intMinutes = Convert.ToInt32(Math.Floor(dblMinutes));
                intSeconds = Convert.ToInt32(Math.Round((dblMinutes - intMinutes) * 60, 0));

                dtTimeSpan = new TimeSpan(0, intMinutes, intSeconds);
            } catch (Exception ex) {
                dtTimeSpan = new TimeSpan(0, 0, 0);
            }

            return dtTimeSpan;

        }

        public override bool ProcessDataFile(string strDataFilePath, clsDatasetFileInfo datasetFileInfo)
        {
            // Returns True if success, False if an error

            var blnSuccess = false;

            var udtHeaderInfo = new clsMassLynxNativeIO.udtMSHeaderInfoType();
            var udtFunctionInfo = new clsMassLynxNativeIO.udtMSFunctionInfoType();

            try {
                var ioFolderInfo = new DirectoryInfo(strDataFilePath);
                datasetFileInfo.FileSystemCreationTime = ioFolderInfo.CreationTime;
                datasetFileInfo.FileSystemModificationTime = ioFolderInfo.LastWriteTime;

                // The acquisition times will get updated below to more accurate values
                datasetFileInfo.AcqTimeStart = datasetFileInfo.FileSystemModificationTime;
                datasetFileInfo.AcqTimeEnd = datasetFileInfo.FileSystemModificationTime;

                datasetFileInfo.DatasetName = GetDatasetNameViaPath(ioFolderInfo.Name);
                datasetFileInfo.FileExtension = ioFolderInfo.Extension;

                // Sum up the sizes of all of the files in this folder
                datasetFileInfo.FileSizeBytes = 0;
                var intFileCount = 0;
                foreach (var item in ioFolderInfo.GetFiles()) {
                    datasetFileInfo.FileSizeBytes += item.Length;

                    if (intFileCount == 0) {
                        // Assign the first file's modification time to .AcqTimeStart and .AcqTimeEnd
                        // Necessary in case _header.txt is missing
                        datasetFileInfo.AcqTimeStart = item.LastWriteTime;
                        datasetFileInfo.AcqTimeEnd = item.LastWriteTime;
                    }

                    if (item.Name.ToLower() == "_header.txt")
                    {
                        // Assign the file's modification time to .AcqTimeStart and .AcqTimeEnd
                        // These will get updated below to more precise values
                        datasetFileInfo.AcqTimeStart = item.LastWriteTime;
                        datasetFileInfo.AcqTimeEnd = item.LastWriteTime;
                    }

                    intFileCount += 1;
                }

                datasetFileInfo.ScanCount = 0;

                blnSuccess = true;

                var objNativeFileIO = new clsMassLynxNativeIO();

                if (objNativeFileIO.GetFileInfo(ioFolderInfo.FullName, ref udtHeaderInfo)) {
                    var dtNewStartDate = System.DateTime.Parse(udtHeaderInfo.AcquDate + " " + udtHeaderInfo.AcquTime);

                    var intFunctionCount = objNativeFileIO.GetFunctionCount(ioFolderInfo.FullName);

                    if (intFunctionCount > 0) {
                        // Sum up the scan count of all of the functions
                        // Additionally, find the largest EndRT value in all of the functions
                        float sngEndRT = 0;
                        var intFunctionNumber = 0;
                        for (intFunctionNumber = 1; intFunctionNumber <= intFunctionCount; intFunctionNumber++) {
                            if (objNativeFileIO.GetFunctionInfo(ioFolderInfo.FullName, 1, ref udtFunctionInfo)) {
                                datasetFileInfo.ScanCount += udtFunctionInfo.ScanCount;
                                if (udtFunctionInfo.EndRT > sngEndRT) {
                                    sngEndRT = udtFunctionInfo.EndRT;
                                }
                            }
                        }

                        if (dtNewStartDate >= MINIMUM_ACCEPTABLE_ACQ_START_TIME) {
                            datasetFileInfo.AcqTimeStart = dtNewStartDate;

                            if (sngEndRT > 0) {
                                datasetFileInfo.AcqTimeEnd = datasetFileInfo.AcqTimeStart.Add(MinutesToTimeSpan(sngEndRT));
                            } else {
                                datasetFileInfo.AcqTimeEnd = datasetFileInfo.AcqTimeStart;
                            }
                        } else {
                            // Keep .AcqTimeEnd as the file modification date
                            // Set .AcqTimeStart based on .AcqEndTime
                            if (sngEndRT > 0) {
                                datasetFileInfo.AcqTimeStart = datasetFileInfo.AcqTimeEnd.Subtract(MinutesToTimeSpan(sngEndRT));
                            } else {
                                datasetFileInfo.AcqTimeEnd = datasetFileInfo.AcqTimeStart;
                            }
                        }
                    } else {
                        if (dtNewStartDate >= MINIMUM_ACCEPTABLE_ACQ_START_TIME) {
                            datasetFileInfo.AcqTimeStart = dtNewStartDate;
                        }
                    }

                } else {
                    // Error getting the header info using clsMassLynxNativeIO
                    // Continue anyway since we've populated some of the values
                }

                blnSuccess = true;

            } catch (Exception ex) {
                blnSuccess = false;
            }

            return blnSuccess;
        }

    }
}