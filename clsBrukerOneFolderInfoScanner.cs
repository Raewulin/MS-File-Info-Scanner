using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
// Started in 2005
//

namespace MSFileInfoScanner
{
    public class clsBrukerOneFolderInfoScanner : clsMSFileInfoProcessorBaseClass
    {

        public const string BRUKER_ONE_FOLDER_NAME = "1";
        private const string BRUKER_LOCK_FILE = "LOCK";
        private const string BRUKER_ACQU_FILE = "acqu";
        private const string BRUKER_ACQU_FILE_ACQ_LINE_START = "##$AQ_DATE= <";

        private const char BRUKER_ACQU_FILE_ACQ_LINE_END = '>';

        private const string TIC_FILE_NUMBER_OF_FILES_LINE_START = "Number of files :";
        private const string TIC_FILE_TIC_FILE_LIST_START = "TIC file list:";
        private const string TIC_FILE_TIC_FILE_LIST_END = "TIC end of file list";

        private const string TIC_FILE_COMMENT_SECTION_END = "CommentEnd";

        private const string PEK_FILE_FILENAME_LINE = "Filename:";

        private readonly DateTime MINIMUM_ACCEPTABLE_ACQ_START_TIME = new DateTime(1975, 1, 1);

        public override string GetDatasetNameViaPath(string dataFilePath)
        {
            var datasetName = string.Empty;

            try
            {
                // The dataset name for a Bruker 1 folder or zipped S folder is the name of the parent directory
                var datasetDirectory = new DirectoryInfo(dataFilePath);
                if (datasetDirectory.Parent != null)
                    datasetName = datasetDirectory.Parent.Name;
            }
            catch (Exception)
            {
                // Ignore errors
            }

            return datasetName;

        }

        public static bool IsZippedSFolder(string filePath)
        {
            var reIsZippedSFolder = new Regex("s[0-9]+\\.zip", RegexOptions.IgnoreCase);
            return reIsZippedSFolder.Match(filePath).Success;
        }

        /// <summary>
        /// Parse out the date from a string of the form
        /// Sat Aug 20 07:56:55 2005
        /// </summary>
        /// <param name="dataLine"></param>
        /// <param name="acqDate"></param>
        /// <returns></returns>
        private bool ParseBrukerDateFromArray(string dataLine, out DateTime acqDate)
        {
            bool success;
            acqDate = DateTime.MinValue;

            try
            {
                var splitLine = dataLine.Split(' ').ToList();

                // Remove any entries from splitLine that are blank
                var lineParts = (from item in splitLine where !string.IsNullOrEmpty(item) select item).ToList();

                if (lineParts.Count >= 5)
                {
                    var startIndex = lineParts.Count - 5;

                    // Construct date in the format yyyy-MMM-dd hh:mm:ss
                    // For example:                 2005-Aug-20 07:56:55
                    var dateText =
                        lineParts[4 + startIndex] + "-" +
                        lineParts[1 + startIndex] + "-" +
                        lineParts[2 + startIndex] + " " +
                        lineParts[3 + startIndex];

                    success = DateTime.TryParse(dateText, out acqDate);
                }
                else
                {
                    success = false;
                }
            }
            catch (Exception)
            {
                // Date parse failed
                success = false;
            }

            return success;

        }

        private bool ParseBrukerAcquFile(string directoryPath, clsDatasetFileInfo datasetFileInfo)
        {
            bool success;

            try
            {
                // Try to open the acqu file
                success = false;
                using (var reader = new StreamReader(Path.Combine(directoryPath, BRUKER_ACQU_FILE)))
                {
                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();

                        if (string.IsNullOrWhiteSpace(dataLine))
                            continue;

                        if (!dataLine.StartsWith(BRUKER_ACQU_FILE_ACQ_LINE_START))
                            continue;

                        // Date line found
                        // It is of the form: ##$AQ_DATE= <Sat Aug 20 07:56:55 2005>
                        dataLine = dataLine.Substring(BRUKER_ACQU_FILE_ACQ_LINE_START.Length).Trim();
                        dataLine = dataLine.TrimEnd(BRUKER_ACQU_FILE_ACQ_LINE_END);

                        success = ParseBrukerDateFromArray(dataLine, out var acqDate);
                        if (success)
                            datasetFileInfo.AcqTimeEnd = acqDate;
                        break;
                    }
                }

            }
            catch (Exception)
            {
                // Error opening the acqu file
                success = false;
            }

            return success;

        }

        private bool ParseBrukerLockFile(string directoryPath, clsDatasetFileInfo datasetFileInfo)
        {
            bool success;

            try
            {
                // Try to open the Lock file
                // The date line is the first (and only) line in the file
                success = false;
                using (var reader = new StreamReader(Path.Combine(directoryPath, BRUKER_LOCK_FILE)))
                {
                    if (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();
                        if (dataLine != null)
                        {
                            // Date line found
                            // It is of the form: wd37119 2208 WD37119\9TOperator Sat Aug 20 06:10:31 2005

                            success = ParseBrukerDateFromArray(dataLine, out var acqDate);
                            if (success)
                                datasetFileInfo.AcqTimeStart = acqDate;
                        }
                    }
                }

            }
            catch (Exception)
            {
                // Error opening the Lock file
                success = false;
            }

            return success;

        }

        /// <summary>
        /// Looks for the s*.zip files to determine the total file size (uncompressed) of all files in all the matching .Zip files
        /// Updates datasetFileInfo.FileSizeBytes with this info, while also updating datasetFileInfo.ScanCount with the total number of files found
        /// Also computes the SHA-1 hash of each file
        /// </summary>
        /// <param name="zippedSFilesFolderInfo"></param>
        /// <param name="datasetFileInfo"></param>
        /// <returns>True if success and also if no matching Zip files were found; returns False if error</returns>
        private bool ParseBrukerZippedSFolders(DirectoryInfo zippedSFilesFolderInfo, clsDatasetFileInfo datasetFileInfo)
        {

            bool success;

            datasetFileInfo.FileSizeBytes = 0;
            datasetFileInfo.ScanCount = 0;

            try
            {
                foreach (var zippedSFile in zippedSFilesFolderInfo.GetFiles("s*.zip"))
                {
                    // Get the info on each zip file

                    using (var zipFileReader = new Ionic.Zip.ZipFile(zippedSFile.FullName))
                    {
                        foreach (var zipEntry in zipFileReader.Entries)
                        {
                            datasetFileInfo.FileSizeBytes += zipEntry.UncompressedSize;
                            datasetFileInfo.ScanCount += 1;
                        }
                    }

                    if (mDisableInstrumentHash)
                    {
                        mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFileNoHash(zippedSFile);
                    }
                    else
                    {
                        // Compute the SHA-1 hash of the zip file (e.g. s001.zip)
                        mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFile(zippedSFile);
                    }

                }
                success = true;

            }
            catch (Exception)
            {
                success = false;
            }

            return success;

        }

        private bool ParseICRDirectory(DirectoryInfo icrDirectoryInfo, clsDatasetFileInfo datasetFileInfo)
        {
            // Look for and open the .Pek file in icrDirectoryInfo
            // Count the number of PEK_FILE_FILENAME_LINE lines

            var fileListCount = 0;
            var success = false;

            foreach (var pekFile in icrDirectoryInfo.GetFiles("*.pek"))
            {
                try
                {
                    // Try to open the PEK file
                    fileListCount = 0;
                    using (var reader = new StreamReader(pekFile.OpenRead()))
                    {
                        while (!reader.EndOfStream)
                        {
                            var dataLine = reader.ReadLine();

                            if (string.IsNullOrWhiteSpace(dataLine))
                                continue;

                            if (dataLine.StartsWith(PEK_FILE_FILENAME_LINE))
                            {
                                fileListCount += 1;
                            }
                        }
                    }
                    success = true;

                }
                catch (Exception)
                {
                    // Error opening or parsing the PEK file
                    success = false;
                }

                if (fileListCount > datasetFileInfo.ScanCount)
                {
                    datasetFileInfo.ScanCount = fileListCount;
                }

                // Only parse the first .Pek file found
                break;
            }

            return success;

        }

        private bool ParseTICDirectory(DirectoryInfo ticDirectoryInfo, clsDatasetFileInfo datasetFileInfo, out DateTime ticModificationDate)
        {
            // Look for and open the .Tic file in ticDirectoryInfo and look for the line listing the number of files
            // As a second validation, count the number of lines between TIC_FILE_TIC_FILE_LIST_START and TIC_FILE_TIC_FILE_LIST_END

            var fileListCount = 0;
            var parsingTICFileList = false;
            var success = false;

            ticModificationDate = DateTime.MinValue;

            foreach (var ticFile in ticDirectoryInfo.GetFiles("*.tic"))
            {
                try
                {
                    // Try to open the TIC file
                    fileListCount = 0;
                    using (var reader = new StreamReader(ticFile.OpenRead()))
                    {
                        while (!reader.EndOfStream)
                        {
                            var dataLine = reader.ReadLine();

                            if (string.IsNullOrWhiteSpace(dataLine))
                                continue;

                            if (parsingTICFileList)
                            {
                                if (dataLine.StartsWith(TIC_FILE_TIC_FILE_LIST_END))
                                {
                                    parsingTICFileList = false;
                                    break;
                                }

                                if (dataLine == TIC_FILE_COMMENT_SECTION_END)
                                {
                                    // Found the end of the text section; exit the loop
                                    break;
                                }

                                fileListCount += 1;
                            }
                            else
                            {
                                if (dataLine.StartsWith(TIC_FILE_NUMBER_OF_FILES_LINE_START))
                                {
                                    // Number of files line found
                                    // Parse out the file count
                                    datasetFileInfo.ScanCount = int.Parse(dataLine.Substring(TIC_FILE_NUMBER_OF_FILES_LINE_START.Length).Trim());
                                }
                                else if (dataLine.StartsWith(TIC_FILE_TIC_FILE_LIST_START))
                                {
                                    parsingTICFileList = true;
                                }
                                else if (dataLine == TIC_FILE_COMMENT_SECTION_END)
                                {
                                    // Found the end of the text section; exit the loop
                                    break;
                                }
                            }
                        }
                    }
                    success = true;

                    ticModificationDate = ticFile.LastWriteTime;

                }
                catch (Exception)
                {
                    // Error opening or parsing the TIC file
                    success = false;
                }

                if (fileListCount > datasetFileInfo.ScanCount)
                {
                    datasetFileInfo.ScanCount = fileListCount;
                }

                // Only parse the first .Tic file found
                break;
            }

            return success;

        }

        /// <summary>
        /// // Process a Bruker 1 folder or Bruker s001.zip file, specified by dataFilePath
        /// </summary>
        /// <param name="dataFilePath">Bruker 1 folder path or Bruker s001.zip file</param>
        /// <param name="datasetFileInfo"></param>
        /// <returns>True if success, False if an error</returns>
        /// <remarks>If a Bruker 1 folder, it must contain file acqu and typically contains file LOCK</remarks>
        public override bool ProcessDataFile(string dataFilePath, clsDatasetFileInfo datasetFileInfo)
        {

            ResetResults();

            DirectoryInfo zippedSFilesFolderInfo = null;

            var scanCountSaved = 0;
            var ticModificationDate = DateTime.MinValue;

            var parsingBrukerOneFolder = false;
            bool success;

            try
            {
                // Determine whether dataFilePath points to a file or a folder
                // See if dataFilePath points to a valid file
                var brukerDatasetFile = new FileInfo(dataFilePath);

                if (brukerDatasetFile.Exists)
                {
                    // Parsing a zipped S folder
                    parsingBrukerOneFolder = false;

                    // The dataset name is equivalent to the name of the folder containing dataFilePath
                    zippedSFilesFolderInfo = brukerDatasetFile.Directory;
                    success = true;

                    // Cannot determine accurate acquisition start or end times
                    // We have to assign a date, so we'll assign the date for the zipped s-folder
                    datasetFileInfo.AcqTimeStart = brukerDatasetFile.LastWriteTime;
                    datasetFileInfo.AcqTimeEnd = brukerDatasetFile.LastWriteTime;

                    if (IsZippedSFolder(brukerDatasetFile.FullName))
                    {
                        // AddInstrumentFile will be called in ParseBrukerZippedSFolders
                        // Do not call it now
                    }
                    else
                    {
                        if (mDisableInstrumentHash)
                        {
                            mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFileNoHash(brukerDatasetFile);
                        }
                        else
                        {
                            // Compute the SHA-1 hash of the bruker dataset file
                            mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFile(brukerDatasetFile);
                        }
                    }
                }
                else
                {
                    // Assuming it's a "1" folder
                    parsingBrukerOneFolder = true;

                    zippedSFilesFolderInfo = new DirectoryInfo(dataFilePath);
                    if (zippedSFilesFolderInfo.Exists)
                    {
                        // Determine the dataset name by looking up the name of the parent folder of dataFilePath
                        zippedSFilesFolderInfo = zippedSFilesFolderInfo.Parent;
                        success = true;
                    }
                    else
                    {
                        success = false;
                    }

                    var filesToFind = new List<string> {"fid", "ser"};
                    var instrumentFileAdded = false;

                    foreach (var fileToFind in filesToFind)
                    {
                        var instrumentDataFile = new FileInfo(Path.Combine(dataFilePath, fileToFind));
                        if (!instrumentDataFile.Exists)
                            continue;

                        if (mDisableInstrumentHash)
                        {
                            mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFileNoHash(instrumentDataFile);
                        }
                        else
                        {
                            // Compute the SHA-1 hash of the fid or ser file
                            mDatasetStatsSummarizer.DatasetFileInfo.AddInstrumentFile(instrumentDataFile);
                        }
                        instrumentFileAdded = true;
                    }

                    // Look for a fid or ser file
                    if (!instrumentFileAdded)
                    {
                        AddLargestInstrumentFile(zippedSFilesFolderInfo);
                    }
                }

                if (success && zippedSFilesFolderInfo != null)
                {
                    datasetFileInfo.FileSystemCreationTime = zippedSFilesFolderInfo.CreationTime;
                    datasetFileInfo.FileSystemModificationTime = zippedSFilesFolderInfo.LastWriteTime;

                    // The acquisition times will get updated below to more accurate values
                    datasetFileInfo.AcqTimeStart = datasetFileInfo.FileSystemModificationTime;
                    datasetFileInfo.AcqTimeEnd = datasetFileInfo.FileSystemModificationTime;

                    datasetFileInfo.DatasetName = zippedSFilesFolderInfo.Name;
                    datasetFileInfo.FileExtension = string.Empty;
                    datasetFileInfo.FileSizeBytes = 0;
                    datasetFileInfo.ScanCount = 0;
                }
            }
            catch (Exception)
            {
                success = false;
            }

            if (success && parsingBrukerOneFolder)
            {
                // Parse the Acqu File to populate .AcqTimeEnd
                success = ParseBrukerAcquFile(dataFilePath, datasetFileInfo);

                if (success)
                {
                    // Parse the Lock file to populate.AcqTimeStart
                    success = ParseBrukerLockFile(dataFilePath, datasetFileInfo);

                    if (!success)
                    {
                        // Use the end time as the start time
                        datasetFileInfo.AcqTimeStart = datasetFileInfo.AcqTimeEnd;
                        success = true;
                    }
                }
            }

            if (success)
            {
                // Look for the zipped S folders in zippedSFilesFolderInfo
                try
                {
                    success = ParseBrukerZippedSFolders(zippedSFilesFolderInfo, datasetFileInfo);
                    scanCountSaved = datasetFileInfo.ScanCount;
                }
                catch (Exception)
                {
                    // Error parsing zipped S Folders; do not abort
                }

                try
                {
                    success = false;

                    if (zippedSFilesFolderInfo != null)
                    {
                        // Look for the TIC* folder to obtain the scan count from a .Tic file
                        // If the Scan Count in the TIC is larger than the scan count from ParseBrukerZippedSFolders,
                        //  then we'll use that instead
                        foreach (var subFolder in zippedSFilesFolderInfo.GetDirectories("TIC*"))
                        {
                            success = ParseTICDirectory(subFolder, datasetFileInfo, out ticModificationDate);

                            if (success)
                            {
                                // Successfully parsed a TIC folder; do not parse any others
                                break;
                            }
                        }
                    }

                    if (!success)
                    {
                        // TIC directory not found; see if a .TIC file is present in zippedSFilesFolderInfo
                        success = ParseTICDirectory(zippedSFilesFolderInfo, datasetFileInfo, out ticModificationDate);
                    }

                    if (success && !parsingBrukerOneFolder && ticModificationDate >= MINIMUM_ACCEPTABLE_ACQ_START_TIME)
                    {
                        // If tICModificationDate is earlier than .AcqTimeStart then update to ticModificationDate
                        if (ticModificationDate < datasetFileInfo.AcqTimeStart)
                        {
                            datasetFileInfo.AcqTimeStart = ticModificationDate;
                            datasetFileInfo.AcqTimeEnd = ticModificationDate;
                        }
                    }

                    if (!success && zippedSFilesFolderInfo != null)
                    {
                        // .Tic file not found in zippedSFilesFolderInfo
                        // Look for an ICR* folder to obtain the scan count from a .Pek file
                        foreach (var subFolder in zippedSFilesFolderInfo.GetDirectories("ICR*"))
                        {
                            success = ParseICRDirectory(subFolder, datasetFileInfo);

                            if (success)
                            {
                                // Successfully parsed an ICR folder; do not parse any others
                                break;
                            }
                        }
                    }

                    if (success)
                    {
                        if (scanCountSaved > datasetFileInfo.ScanCount)
                        {
                            datasetFileInfo.ScanCount = scanCountSaved;
                        }
                    }
                    else
                    {
                        // Set success to true anyway since we do have enough information to save the MS file info
                        success = true;
                    }

                }
                catch (Exception)
                {
                    // Error parsing the TIC* or ICR* folders; do not abort
                }

                // Validate datasetFileInfo.AcqTimeStart vs. datasetFileInfo.AcqTimeEnd
                if (datasetFileInfo.AcqTimeEnd >= MINIMUM_ACCEPTABLE_ACQ_START_TIME)
                {
                    if (datasetFileInfo.AcqTimeStart > datasetFileInfo.AcqTimeEnd)
                    {
                        // Start time cannot be greater than the end time
                        datasetFileInfo.AcqTimeStart = datasetFileInfo.AcqTimeEnd;
                    }
                    else if (datasetFileInfo.AcqTimeStart < MINIMUM_ACCEPTABLE_ACQ_START_TIME)
                    {
                        datasetFileInfo.AcqTimeStart = datasetFileInfo.AcqTimeEnd;
                    }
                }
            }

            PostProcessTasks();

            return success;

        }

    }
}
