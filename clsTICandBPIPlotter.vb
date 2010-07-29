﻿Option Strict On

Public Class clsTICandBPIPlotter

#Region "Constants, Enums, Structures"
    Public Enum eOutputFileTypes
        TIC = 0
        BPIMS = 1
        BPIMSn = 2
    End Enum

    Protected Structure udtOutputFileInfoType
        Public FileType As eOutputFileTypes
        Public FileName As String
        Public FilePath As String
    End Structure
#End Region

#Region "Member variables"
    Protected mBPI As clsChromatogramInfo
    Protected mTIC As clsChromatogramInfo

    Protected mRecentFiles As System.Collections.Generic.List(Of udtOutputFileInfoType)
#End Region

    Public Sub New()
        mRecentFiles = New System.Collections.Generic.List(Of udtOutputFileInfoType)
        Me.Reset()
    End Sub

    Public Sub AddData(ByVal intScanNumber As Integer, _
                       ByVal intMSLevel As Integer, _
                       ByVal sngScanTimeMinutes As Single, _
                       ByVal dblBPI As Double, _
                       ByVal dblTIC As Double)

        mBPI.AddPoint(intScanNumber, intMSLevel, sngScanTimeMinutes, dblBPI)
        mTIC.AddPoint(intScanNumber, intMSLevel, sngScanTimeMinutes, dblTIC)

    End Sub

    Protected Sub AddRecentFile(ByVal strFilePath As String, ByVal eFileType As eOutputFileTypes)
        Dim udtOutputFileInfo As udtOutputFileInfoType

        udtOutputFileInfo.FileType = eFileType
        udtOutputFileInfo.FileName = System.IO.Path.GetFileName(strFilePath)
        udtOutputFileInfo.FilePath = strFilePath

        mRecentFiles.Add(udtOutputFileInfo)
    End Sub

    ''' <summary>
    ''' Returns the file name of the recently saved file of the given type
    ''' </summary>
    ''' <param name="eFileType">File type to find</param>
    ''' <returns>File name if found; empty string if this file type was not saved</returns>
    ''' <remarks>The list of recent files gets cleared each time you call Save2DPlots() or Reset()</remarks>
    Public Function GetRecentFileInfo(ByVal eFileType As eOutputFileTypes) As String
        Dim intIndex As Integer
        For intIndex = 0 To mRecentFiles.Count - 1
            If mRecentFiles(intIndex).FileType = eFileType Then
                Return mRecentFiles(intIndex).FileName
            End If
        Next
        Return String.Empty
    End Function

    ''' <summary>
    ''' Returns the file name and path of the recently saved file of the given type
    ''' </summary>
    ''' <param name="eFileType">File type to find</param>
    ''' <param name="strFileName">File name (output)</param>
    ''' <param name="strFilePath">File Path (output)</param>
    ''' <returns>True if a match was found; otherwise returns false</returns>
    ''' <remarks>The list of recent files gets cleared each time you call Save2DPlots() or Reset()</remarks>
    Public Function GetRecentFileInfo(ByVal eFileType As eOutputFileTypes, ByRef strFileName As String, ByRef strFilePath As String) As Boolean
        Dim intIndex As Integer
        For intIndex = 0 To mRecentFiles.Count - 1
            If mRecentFiles(intIndex).FileType = eFileType Then
                strFileName = mRecentFiles(intIndex).FileName
                strFilePath = mRecentFiles(intIndex).FilePath
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>
    ''' Plots a BPI or TIC chromatogram
    ''' </summary>
    ''' <param name="objData">Data to display</param>
    ''' <param name="strTitle">Title of the plot</param>
    ''' <param name="intMSLevelFilter">0 to use all of the data, 1 to use data from MS scans, 2 to use data from MS2 scans, etc.</param>
    ''' <returns>Zedgraph plot</returns>
    ''' <remarks></remarks>
    Private Function InitializeGraphPane(ByRef objData As clsChromatogramInfo, _
                                         ByVal strTitle As String, _
                                         ByVal intMSLevelFilter As Integer) As ZedGraph.GraphPane

        Const FONT_SIZE_BASE As Integer = 11

        Dim myPane As New ZedGraph.GraphPane

        Dim objPoints As ZedGraph.PointPairList

        Dim intIndex As Integer

        Dim intMaxScan As Integer
        Dim dblScanTimeMax As Double
        Dim dblMaxIntensity As Double


        ' Instantiate the ZedGraph object to track the points
        objPoints = New ZedGraph.PointPairList

        intMaxScan = 0
        dblScanTimeMax = 0
        dblMaxIntensity = 0


        With objData

            For intIndex = 0 To .ScanCount - 1
                If intMSLevelFilter = 0 OrElse _
                   .ScanMSLevel(intIndex) = intMSLevelFilter OrElse _
                   intMSLevelFilter = 2 And .ScanMSLevel(intIndex) >= 2 Then

                    objPoints.Add(New ZedGraph.PointPair(.ScanNum(intIndex), .ScanIntensity(intIndex)))

                    If .ScanTimeMinutes(intIndex) > dblScanTimeMax Then
                        dblScanTimeMax = .ScanTimeMinutes(intIndex)
                    End If

                    If .ScanNum(intIndex) > intMaxScan Then
                        intMaxScan = .ScanNum(intIndex)
                    End If

                    If .ScanIntensity(intIndex) > dblMaxIntensity Then
                        dblMaxIntensity = .ScanIntensity(intIndex)
                    End If
                End If
            Next intIndex

        End With

        If objPoints.Count = 0 Then
            ' Nothing to plot
            Return myPane
        End If

        ' Round intMaxScan down to the nearest multiple of 10
        intMaxScan = CInt(Math.Ceiling(intMaxScan / 10.0) * 10)

        ' Multiple dblMaxIntensity by 2% and then round up to the nearest integer
        dblMaxIntensity = CLng(Math.Ceiling(dblMaxIntensity * 1.02))

        ' Set the titles and axis labels
        myPane.Title.Text = String.Copy(strTitle)
        myPane.XAxis.Title.Text = "LC Scan Number"
        myPane.YAxis.Title.Text = "Intensity"

        ' Generate a black curve with no symbols
        Dim myCurve As ZedGraph.LineItem
        myPane.CurveList.Clear()

        If objPoints.Count > 0 Then
            myCurve = myPane.AddCurve(strTitle, objPoints, System.Drawing.Color.Black, ZedGraph.SymbolType.None)

            myCurve.Line.Width = 1
        End If

        ' Possibly add a label showing the maximum elution time
        If dblScanTimeMax > 0 Then

            Dim objScanTimeMaxText As New ZedGraph.TextObj(dblScanTimeMax.ToString("0") & " minutes", 1, 1, ZedGraph.CoordType.PaneFraction)

            With objScanTimeMaxText
                .FontSpec.Angle = 0
                .FontSpec.FontColor = Drawing.Color.Black
                .FontSpec.IsBold = False
                .FontSpec.Size = FONT_SIZE_BASE
                .FontSpec.Border.IsVisible = False
                .FontSpec.Fill.IsVisible = False
                .Location.AlignH = ZedGraph.AlignH.Right
                .Location.AlignV = ZedGraph.AlignV.Bottom
            End With
            myPane.GraphObjList.Add(objScanTimeMaxText)

        End If

        ' Hide the x and y axis grids
        myPane.XAxis.MajorGrid.IsVisible = False
        myPane.YAxis.MajorGrid.IsVisible = False

        ' Set the X-axis to display unmodified scan numbers (by default, ZedGraph scales them to a range between 0 and 10)
        myPane.XAxis.Scale.Mag = 0
        myPane.XAxis.Scale.MagAuto = False
        myPane.XAxis.Scale.MaxGrace = 0

        ' Override the auto-computed axis range
        myPane.XAxis.Scale.Min = 0
        myPane.XAxis.Scale.Max = intMaxScan

        '' Could set the Y-axis to display unmodified m/z values
        'myPane.YAxis.Scale.Mag = 0
        'myPane.YAxis.Scale.MagAuto = False
        'myPane.YAxis.Scale.MaxGrace = 0.01

        ' Override the auto-computed axis range
        myPane.YAxis.Scale.Min = 0
        myPane.YAxis.Scale.Max = dblMaxIntensity
        myPane.YAxis.Title.IsOmitMag = True

        AddHandler myPane.YAxis.ScaleFormatEvent, AddressOf ZedGraphYScaleFormatter

        ' Align the Y axis labels so they are flush to the axis
        myPane.YAxis.Scale.Align = ZedGraph.AlignP.Inside

        ' Adjust the font sizes
        myPane.XAxis.Title.FontSpec.Size = FONT_SIZE_BASE
        myPane.XAxis.Title.FontSpec.IsBold = True
        myPane.XAxis.Scale.FontSpec.Size = FONT_SIZE_BASE

        myPane.YAxis.Title.FontSpec.Size = FONT_SIZE_BASE
        myPane.YAxis.Title.FontSpec.IsBold = True
        myPane.YAxis.Scale.FontSpec.Size = FONT_SIZE_BASE

        myPane.Title.FontSpec.Size = FONT_SIZE_BASE + 1
        myPane.Title.FontSpec.IsBold = True

        ' Fill the axis background with a gradient
        myPane.Chart.Fill = New ZedGraph.Fill(System.Drawing.Color.White, System.Drawing.Color.FromArgb(255, 230, 230, 230), 45.0F)

        ' Could use the following to simply fill with white
        'myPane.Chart.Fill = New ZedGraph.Fill(Drawing.Color.White)

        ' Hide the legend
        myPane.Legend.IsVisible = False

        ' Force a plot update
        myPane.AxisChange()

        Return myPane

    End Function

    Public Sub Reset()

        If mBPI Is Nothing Then
            mBPI = New clsChromatogramInfo
            mTIC = New clsChromatogramInfo
        Else
            mBPI.Initialize()
            mTIC.Initialize()
        End If

        mRecentFiles.Clear()

    End Sub

    Public Function SaveTICAndBPIPlotFiles(ByVal strDatasetName As String, _
                                           ByVal strOutputFolderPath As String, _
                                           ByRef strErrorMessage As String) As Boolean

        Dim myPane As ZedGraph.GraphPane
        Dim strPNGFilePath As String
        Dim blnSuccess As Boolean

        Try
            strErrorMessage = String.Empty

            mRecentFiles.Clear()

            ' Check whether all of the spectra have .MSLevel = 0
            ' If they do, change the level to 1
            ValidateMSLevel(mBPI)
            ValidateMSLevel(mTIC)

            myPane = InitializeGraphPane(mBPI, strDatasetName & " - BPI - MS Spectra", 1)
            If myPane.CurveList.Count > 0 Then
                strPNGFilePath = System.IO.Path.Combine(strOutputFolderPath, strDatasetName & "_BPI_MS.png")
                myPane.GetImage(1024, 600, 300, False).Save(strPNGFilePath, System.Drawing.Imaging.ImageFormat.Png)
                AddRecentFile(strPNGFilePath, eOutputFileTypes.BPIMS)
            End If

            myPane = InitializeGraphPane(mBPI, strDatasetName & " - BPI - MS2 Spectra", 2)
            If myPane.CurveList.Count > 0 Then
                strPNGFilePath = System.IO.Path.Combine(strOutputFolderPath, strDatasetName & "_BPI_MSn.png")
                myPane.GetImage(1024, 600, 300, False).Save(strPNGFilePath, System.Drawing.Imaging.ImageFormat.Png)
                AddRecentFile(strPNGFilePath, eOutputFileTypes.BPIMSn)
            End If

            myPane = InitializeGraphPane(mTIC, strDatasetName & " - TIC - All Spectra", 0)
            If myPane.CurveList.Count > 0 Then
                strPNGFilePath = System.IO.Path.Combine(strOutputFolderPath, strDatasetName & "_TIC.png")
                myPane.GetImage(1024, 600, 300, False).Save(strPNGFilePath, System.Drawing.Imaging.ImageFormat.Png)
                AddRecentFile(strPNGFilePath, eOutputFileTypes.TIC)
            End If

            blnSuccess = True
        Catch ex As System.Exception
            strErrorMessage = ex.Message
            blnSuccess = False
        End Try

        Return blnSuccess

    End Function

    Protected Sub ValidateMSLevel(ByVal udtChrom As clsChromatogramInfo)
        Dim intIndex As Integer
        Dim blnMSLevelDefined As Boolean

        For intIndex = 0 To udtChrom.ScanCount - 1
            If udtChrom.ScanMSLevel(intIndex) > 0 Then
                blnMSLevelDefined = True
                Exit For
            End If
        Next intIndex

        If Not blnMSLevelDefined Then
            ' Set the MSLevel to 1 for all scans
            For intIndex = 0 To udtChrom.ScanCount - 1
                udtChrom.ScanMSLevel(intIndex) = 1
            Next intIndex
        End If

    End Sub

    Private Function ZedGraphYScaleFormatter(ByVal pane As ZedGraph.GraphPane, _
                                               ByVal axis As ZedGraph.Axis, _
                                               ByVal val As Double, _
                                               ByVal index As Int32) As String
        If val = 0 Then
            Return "0"
        Else
            Return val.ToString("0.00E+00")
        End If

    End Function

    Protected Class clsChromatogramInfo

        Public ScanCount As Integer
        Public ScanNum() As Integer
        Public ScanTimeMinutes() As Single
        Public ScanIntensity() As Double
        Public ScanMSLevel() As Integer

        Public Sub New()
            Me.Initialize()
        End Sub

        Public Sub AddPoint(ByVal intScanNumber As Integer, _
                            ByVal intMSLevel As Integer, _
                            ByVal sngScanTimeMinutes As Single, _
                            ByVal dblIntensity As Double)

            If Me.ScanCount >= Me.ScanNum.Length Then
                ReDim Preserve Me.ScanNum(Me.ScanNum.Length * 2 - 1)
                ReDim Preserve Me.ScanTimeMinutes(Me.ScanNum.Length - 1)
                ReDim Preserve Me.ScanIntensity(Me.ScanNum.Length - 1)
                ReDim Preserve Me.ScanMSLevel(Me.ScanNum.Length - 1)
            End If

            Me.ScanNum(Me.ScanCount) = intScanNumber
            Me.ScanTimeMinutes(Me.ScanCount) = sngScanTimeMinutes
            Me.ScanIntensity(Me.ScanCount) = dblIntensity
            Me.ScanMSLevel(Me.ScanCount) = intMSLevel

            Me.ScanCount += 1
        End Sub

        Public Sub Initialize()
            ScanCount = 0
            ReDim ScanNum(9)
            ReDim ScanTimeMinutes(9)
            ReDim ScanIntensity(9)
            ReDim ScanMSLevel(9)
        End Sub

        Public Sub TrimArrays()
            ReDim Preserve ScanNum(ScanCount - 1)
            ReDim Preserve ScanTimeMinutes(ScanCount - 1)
            ReDim Preserve ScanIntensity(ScanCount - 1)
            ReDim Preserve ScanMSLevel(ScanCount - 1)
        End Sub

    End Class
End Class