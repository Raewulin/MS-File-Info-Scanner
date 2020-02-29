# Overview

The MS File Info Scanner can be used to scan a series of MS data files 
(or data directories) and extract the acquisition start and end times, 
number of spectra, and the total size of the data, saving the values 
in the file DatasetTimeFile.txt

Supported file types are:
* Thermo .raw files 
* Agilent Ion Trap (.d directories)
* Agilent or QStar .wiff files
* Waters (Masslynx) .raw folders
* Bruker 1 directories
* Bruker XMass analysis.baf
* .UIMF files (IMS or SLIM)
* DeconTools _isos.csv files (uses the _scans.csv file for elution time info)

## Example QC Graphics

Links to example QC graphic output files can be found on the documentation page at\
[https://pnnl-comp-mass-spec.github.io/MS-File-Info-Scanner](https://pnnl-comp-mass-spec.github.io/MS-File-Info-Scanner/index.html#results)

## Console Switches

MSFileInfoScanner is a command line application.\
Syntax:

```
MSFileInfoScanner.exe
 /I:InputFileNameOrDirectoryPath [/O:OutputFolderName]
 [/P:ParamFilePath] [/S[:MaxLevel]] [/IE] [/L:LogFilePath]
 [/LC[:MaxPointsToPlot]] [/NoTIC] [/LCGrad]
 [/DI] [/SS] [/QS] [/CC]
 [/MS2MzMin:MzValue] [/NoHash]
 [/DST:DatasetStatsFileName]
 [/ScanStart:0] [/ScanEnd:0] [/Debug]
 [/C] [/M:nnn] [/H] [/QZ]
 [/CF] [/R] [/Z]
 [/PostToDMS] [/PythonPlot]
```

Use `/I` to specify the name of a file or directory to scan
* The path can contain the wildcard character *

The output directory name is optional
* If omitted, the output files will be created in the program directory

The parameter file switch `/P` is optional
* If provided, it should point to a valid XML parameter file. If omitted, defaults are used

Use `/S` to process all valid files in the input directory and subdirectories
* Include a number after /S (like `/S:2`) to limit the level of subdirectories to examine
* Use `/IE` to ignore errors when recursing

Use `/L` to specify the file path for logging messages

Use `/LC` to create 2D LCMS plots (this process could take several minutes for each dataset)
* By default, plots the top 200000 points
* To plot the top 20000 points, use `/LC:20000`

Use `/LCDiv` to specify the divisor to use when creating the overview 2D LCMS plots
* By default, uses `/LCDiv:10`
* Use `/LCDiv:0` to disable creation of the overview plots

By default, the MS File Info Scanner creates TIC and BPI plots, showing intensity vs. time
* Use `/NoTIC` to disable creating TIC and BPI plots
* Plots created:
  * Separate BPI plots for MS and MS2 spectra
  * Single TIC plot for all spectra
* For Thermo .raw files where the acquisition software also controlled an LC system, 
  if the .raw file has pressure data (or similar) stored in it, this program will also create plots of pressure vs. time
  * These are labeled "Addnl Plots" in the index.html file
* For .UIMF files, if the file includes pressure information, a pressure vs. time plot will be created
* The software also creates an html file named `index.html` that shows an overview of each plot, plus a table with scan stats 

Use `/LCGrad` to save a series of 2D LC plots, each using a different color scheme
* The default color scheme is OxyPalettes.Jet

Use `/DatasetID:#` to define the dataset's DatasetID value (where # is an integer)
* Only appropriate if processing a single dataset
* If defined, the DatasetID is included in the dataset info XML file

Use `/DI` to create a dataset info XML file for each dataset

Use `/SS` to create a _ScanStats.txt  file for each dataset

Use `/QS` to compute an overall quality score for the data in each datasets

Use `/CC` to check spectral data for whether it is centroided or profile

Use `/MS2MzMin` to specify a minimum m/z value that all MS/MS spectra should have
* Will report an error if any MS/MS spectra have minimum m/z value larger than the threshold
* Useful for validating instrument files where the sample is iTRAQ or TMT labeled and it is important to detect the reporter ions in the MS/MS spectra
* Select the default minimum m/z for iTRAQ (113) using `/MS2MzMin:iTRAQ`
* Select the default mnimum m/z for TMT (126) using `/MS2MzMin:TMT`
* Specify a custom minimum m/z value using `/MS2MzMin:110`

A SHA-1 hash is computed for the primary instrument data file(s)
* Use `/NoHash` to disable this

Use `/DST` to update (or create) a tab-delimited text file with overview stats for the dataset
* If `/DI` is specified, the file will include detailed scan counts
  * Otherwise, it will just have the dataset name, acquisition date, and (if available) sample name and comment
* By default, the file is named MSFileInfo_DatasetStats.txt
  * To override, add the file name after the `/DST` switch, for example `/DST:DatasetStatsFileName.txt`

Use `/ScanStart` and `/ScanEnd` to limit the scan range to process
* Useful for files where the first few scans are corrupt
* For example, to start processing at scan 10, use `/ScanStart:10`

Use `/Debug` to display debug information at the console, including showing the
scan number prior to reading each scan's data

Use `/C` to perform an integrity check on all known file types
* This process will open known file types and verify that they contain the expected data
* This option is only used if you specify an Input Directory and use a wildcard
  * You will typically also want to use `/S` when using `/C`

Use `/M` to define the maximum number of lines to process when checking text or csv files
* Default is `/M:500`

Use `/H` to compute SHA-1 file hashes when verifying file integrity

Use `/QZ` to run a quick zip-file validation test when verifying file integrity
* When defined, the test does not check all data in the .Zip file

Use `/CF` to save/load information from the acquisition time file (cache file)
* This option is auto-enabled if you use `/C`

Use `/R` to reprocess files that are already defined in the acquisition time file

Use `/Z` to reprocess files that are already defined in the acquisition time file
only if their cached size is 0 bytes

Use `/PostToDMS` to store the dataset info in the DMS database
* To customize the server name and/or stored procedure to use for posting, use an XML parameter file
with the following settings 
  * `DSInfoConnectionString`
  * `DSInfoDBPostingEnabled`
  * `DSInfoStoredProcedure`

Use `/PythonPlot` to create plots with Python instead of OxyPlot
* Looks for `python.exe` in directories that start with "Python3" or "Python 3" on Windows, searching below:
  * C:\Program Files
  * C:\Program Files (x86)
  * C:\Users\Username\AppData\Local\Programs
  * C:\ProgramData\Anaconda3
  * C:\
* Assumes Python is at `/usr/bin/python3` on Linux

Known file extensions: .RAW, .WIFF, .BAF, .MCF, .MCF_IDX, .UIMF, .CSV\
Known directory extensions: .D, .RAW

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov \
Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/

## License

MS File Info Scanner is licensed under the 2-Clause BSD License; you may not use this file 
except in compliance with the License. You may obtain a copy of the License at 
https://opensource.org/licenses/BSD-2-Clause

Copyright 2020 Battelle Memorial Institute

RawFileReader reading tool. Copyright � 2016 by Thermo Fisher Scientific, Inc. All rights reserved.
