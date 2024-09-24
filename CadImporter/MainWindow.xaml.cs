using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Globalization;
using Ab4d.OpenCascade;

#if DXENGINE
using SharpDX;
#else
using System.Numerics;
#endif

namespace CadImporter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // When the data types returned by the Ab4d.OpenCascade.CadImporter are changed, then the Ab4d.OpenCascade.CadImporter.CadImporterVersion major or minor version is changed (changed build version does not change the data model).
        private static readonly Version SupportedCadImporterVersion = new Version(0, 1);

        private string? _fileName;

        private Ab4d.OpenCascade.CadImporter? _cadImporter;
        private CadAssembly? _cadAssembly;

        private StringBuilder? _logStringBuilder;

        private List<Vector3> _tempEdgeLinePositions = new List<Vector3>();

        private DateTime _lastEscapeKeyPressedTime;

        private CadPart? _highlightedCadPart;
        private CadFace? _highlightedCadFace;

        private DateTime _lastMouseUpTime;
        //private bool _isCameraRotating;
        //private bool _isCameraMoving;

#if DXENGINE
        private DXEngineSceneView _sceneView; 
#else
        private SharpEngineSceneView _sceneView; 
#endif

        public MainWindow()
        {
            // !!! IMPORTANT !!!
            // No type from Ab4d.OpenCascade should be used in the MainWindow constructor or DXSceneInitialized event handler.
            // If it is used, then the application will try to load the OpenCascade dlls before the InitializeCadImporter method is called.
            // If the OpenCascade dlls are not present in the output folder, then this will throw "Cannot load file or assembly" exception.
            // For example, the initialization of ImporterUnitsComboBox was removed from the code because it uses Ab4d.OpenCascade.CadUnitTypes (the ComboBoxItems are manually defined in XAML instead):
            // ImporterUnitsComboBox.ItemsSource = Enum.GetNames<Ab4d.OpenCascade.CadUnitTypes>();
            // ImporterUnitsComboBox.SelectedIndex = 2; // Millimeter


            // The CadImporter from Ab4d.OpenCascade library requires many native OpenCascade dlls.
            // To see a list of all required files check the "OpenCascade required files.txt" file.
            // 
            // The required files can be downloaded from https://github.com/Open-Cascade-SAS/OCCT/releases/ web page.
            // To get the required OpenCascade version, you can read the static Ab4d.OpenCascade.CadImporter.OpenCascadeVersion property.
            // For the required version, download the occt-vc143-64.zip and 3rdparty-vc14-64.zip and extract the zip files.
            // 
            // Then you have multiple options:
            // 
            // 1. Copy the required dlls to the output folder of your application.
            //    This is a recommended option because you can use any type from the Ab4d.OpenCascade anywhere in the application.
            //    This is also recommended when distributing your application.
            //
            // 2. Copy the required dlls to a custom folder and then adjust PATH environment variable or
            //    call system's SetDllDirectory function with the path to the OpenCascade folder.
            //    This option is used in this sample application (this the structure of files in the output folder nicer).
            // 
            // For both methods you can use the "copy-open-cascade-dlls.bat" batch file.
            // Before running it, open the file and update the OCCT and OCCT_THIRD_PARTY paths so that they point to the paths where you have extracted the zip files.
            // 
            // See also the InitializeCadImporter and CreateCadImporter methods below.


            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            InitializeComponent();

#if DXENGINE
            _sceneView = new DXEngineSceneView();
#else
            _sceneView = new SharpEngineSceneView();

            // CameraNavigationCircles and MouseCameraControllerInfo controls are not available in Ab4d.SharpEngine
            ShowCameraAxisPanelCheckBox.Visibility = Visibility.Collapsed;
            ShowCameraNavigationCirclesCheckBox.Visibility = Visibility.Collapsed;
            ShowMouseCameraControllerInfoCheckBox.Visibility = Visibility.Collapsed;
            ViewPanelsTitle.Visibility = Visibility.Collapsed;
#endif

            SceneViewBorder.Child = _sceneView;
            

            // CAD application usually use Z up axis. So set that by default. This can be changed by the user in the Settings.
            _sceneView.UseZUpAxis();


            // Wait until DXEngine is initialized and then load the step file
            _sceneView.SceneViewInitialized += (sender, args) =>
            {
                InitializeCadImporter();
                string fileName = "as1_pe_203.stp";
                //string fileName = "cube.stp";
                fileName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step_files", fileName);

                LoadCadFile(fileName);
            };

            var dragAndDropHelper = new DragAndDropHelper(this, ".step .stp .iges .igs");
            dragAndDropHelper.FileDropped += (sender, args) =>
            {
                DragDropInfoTextBlock.Visibility = Visibility.Collapsed; // Show only until user knows what to do
                LoadCadFile(args.FileName);
            };

            SceneViewBorder.MouseMove += SceneViewBorderOnMouseMove;
            SceneViewBorder.PreviewMouseLeftButtonUp += SceneViewBorderOnMouseUp;

            // Check if ESCAPE key is pressed - on first press deselect the object, on second press show all objects
            this.Focusable = true; // by default Page is not focusable and therefore does not receive keyDown event
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Focus();
        }

        // Prevent inlining so we can update PATH or call SetDllDirectory before any type from Ab4d.OpenCascade is used.
        // This way we can also get an exception in case CadImporter and OpenCascade cannot be loaded
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InitializeCadImporter()
        {
            // First check if OpenCascade dll files are available in the local folder
            var allDlls = System.IO.Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory);

            bool hasLocalOpenCascadeFiles = allDlls.Any(f => f.EndsWith("TKernel.dll"));
            bool hasLocalThirdPartyFiles = allDlls.Any(f => f.EndsWith("FreeImage.dll"));

            if (!hasLocalOpenCascadeFiles || !hasLocalThirdPartyFiles)
            {
                // If not, then try to load OpenCascade dlls from the OpenCascade folder
                var openCascadeFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenCascade\\");

                if (System.IO.Directory.Exists(openCascadeFolder))
                {
                    var pathValue = Environment.GetEnvironmentVariable("PATH");
                    if (pathValue != null && !pathValue.Contains("OpenCascade", StringComparison.OrdinalIgnoreCase))
                    {
                        pathValue += ";" + openCascadeFolder;
                        Environment.SetEnvironmentVariable("PATH", pathValue);
                    }
                }

                // Instead of changing the path, it would be also possible to call system SetDllDirectory.
                // The following is the pinvoke definition of SetDllDirectory:
                // [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                // [return: MarshalAs(UnmanagedType.Bool)]
                // static extern bool SetDllDirectory(string lpPathName);
                //
                // Then you can call it with:
                // SetDllDirectory(openCascadeFolder);
            }

            try
            {
                CreateCadImporter();
            }
            catch (System.IO.FileNotFoundException ex)
            {
                // In case of EEFileLoadException exception (thrown when native debugging is enabled), the folder with 3rd party dlls is probably not found
                // See installation instructions in readme.txt on how to define the folder structure

                string errorMessage = "Cannot load native OpenCASCADE library:\r\n" + 
                                      ex.Message + 
                                      "\r\n\r\nTo solve that do one of the following:\r\nCopy all required OpenCascade dlls to the output path (same as .exe).\r\nCall system's SetDllDirectory function and set the path to OpenCascade folder.\r\nSet PATH environment to OpenCascade bin folder and its third-party folder.\r\n\r\nSee readme.txt for more info.";

                MessageBox.Show(errorMessage);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error initializing Ab4d.OpenCascade:\r\n" + ex.Message);
            }
        }


        // Create an instance of CadImporter in a method that is not inlined
        // so that "Cannot load file or assembly" exception is thrown from try...catch in the InitializeCadImporter method.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateCadImporter()
        {
            // Check that we are using the correct version of Ab4d.OpenCascade.CadImporter.
            // When the data types returned by the Ab4d.OpenCascade.CadImporter are changed,
            // then the Ab4d.OpenCascade.CadImporter.CadImporterVersion major or minor version is changed (changed build version does not change the data model).
            if (Ab4d.OpenCascade.CadImporter.CadImporterVersion.Major != SupportedCadImporterVersion.Major ||
                Ab4d.OpenCascade.CadImporter.CadImporterVersion.Minor != SupportedCadImporterVersion.Minor)
            {
                MessageBox.Show($"The Cad importer is using a different version of Ab4d.OpenCascade library ({Ab4d.OpenCascade.CadImporter.CadImporterVersion}) that is supported by this application ({SupportedCadImporterVersion}). Please update the Cad importer or use the Ab4d.OpenCascade library that is compatible by this application.");
                return;
            }

            // You can get the required OpenCascade version by:
            //var requiredOpenCascadeVersion = Ab4d.OpenCascade.CadImporter.OpenCascadeVersion;

            _sceneView.ActivateCadImporter();

            // Create an instance of CadImporter
            _cadImporter = new Ab4d.OpenCascade.CadImporter();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LoadCadFile(string fileName)
        {
            _sceneView.ClearScene();

            ElementsTreeView.Items.Clear();

            ClearSelection();
            ClearHighlightPartOrFace();

            _fileName = null;
            _cadAssembly = null;

            InfoTextBox.Text = "";

            if (_cadImporter == null)
                return;


            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    // Set ImporterSettings:

                    var importerSettings = new ImporterSettings();
                    importerSettings.DefaultColor = new float[] { 0.5f, 0.5f, 0.5f, 1 };

                    // When true, then exact Volume (for CadParts), SurfaceArea (for CadParts and CadFace) and EdgeLengths (for CadFace) are calculated. It may take some time to calculate those values.
                    importerSettings.CalculateShapeProperties = CalculateShapePropertiesCheckBox.IsChecked ?? false;

                    // By default, the CadImporter generates the triangulated mesh and interpolated edge positions.
                    // If you only need to get the original CAD objects and structure, you can set the following two properties to false:
                    //importerSettings.GenerateTriangulatedMeshes = false;
                    //importerSettings.GenerateInterpolatedEdgePositions = false;

                    // Sets the units in which the imported CAD objects will be
                    if (ImporterUnitsComboBox.SelectedIndex != -1)
                        importerSettings.ImportedUnits = Enum.GetValues<Ab4d.OpenCascade.CadUnitTypes>()[ImporterUnitsComboBox.SelectedIndex + 1]; // +1 because Undefined is skipped in xaml
                    
                    // There are many parameters for MeshingSettings.
                    // See OpenCascade's IMeshTools_Parameters: https://dev.opencascade.org/doc/refman/html/struct_i_mesh_tools___parameters.html
                    var meshDeflection = GetSelectedComboBoxDoubleValue(MeshTriangulationDeflationComboBox);
                    importerSettings.MeshingSettings.Angle = meshDeflection;
                    importerSettings.MeshingSettings.Deflection = meshDeflection;

                    // The following settings define how the edge lines are interpolated
                    // Changing CurvatureDeflection has the biggest effect, for example:
                    // For Circle with CurvatureDeflection with 0.1 80 positions are generated;
                    // For Circle with CurvatureDeflection with 0.5 36 positions are generated

                    var edgeDeflection = GetSelectedComboBoxDoubleValue(EdgeInterpolationDeflationComboBox);
                    importerSettings.CurveInterpolationSettings.AngularDeflection = edgeDeflection;   // angular deflection in radians (default value is 0.1)
                    importerSettings.CurveInterpolationSettings.CurvatureDeflection = edgeDeflection; // linear deflection (default value is 0.1)
                    importerSettings.CurveInterpolationSettings.MinimumOfPoints = 2;              // minimum number of points  (default value is 2)
                    importerSettings.CurveInterpolationSettings.Tolerance = 0.1;                  // tolerance in curve parametric scope (original parameter name: theUTo; default value is 1.0e-9)
                    importerSettings.CurveInterpolationSettings.MinimalLength = 0.1;              // minimal length (default value is 1.0e-7)
                    
                    if (IsLoggingCheckBox.IsChecked ?? false)
                    {
                        if (_logStringBuilder == null)
                            _logStringBuilder = new StringBuilder();
                        else
                            _logStringBuilder.Clear();

                        importerSettings.LogAction = AddCadImporterLogAction;
                    }
                    else
                    {
                        importerSettings.LogAction = null;
                    }

                    _cadImporter.Initialize(importerSettings);

                    // Load file

                    var extension = System.IO.Path.GetExtension(fileName);
                    if (extension.Equals(".iges", StringComparison.OrdinalIgnoreCase) || extension.Equals(".igs", StringComparison.OrdinalIgnoreCase))
                        _cadAssembly = _cadImporter.ImportIgesFile(fileName);
                    else
                        _cadAssembly = _cadImporter.ImportStepFile(fileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error importing file:\r\n" + ex.Message);
                }

                if (_cadAssembly == null)
                {
                    this.Title = "CAD Importer";
                    return;
                }

                _sceneView.ProcessCadParts(_cadAssembly);
                
                FillTreeView(_cadAssembly);

                ResetCamera(); // Show all objects


                if (IsLoggingCheckBox.IsChecked ?? false)
                    ShowCadImporterLogMessages();

                if (DumpImportedObjectsCheckBox.IsChecked ?? false)
                    DumpLoadedParts(_cadAssembly);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading CAD file {System.IO.Path.GetFileName(fileName)}:\r\n{ex.Message}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            
            _fileName = fileName;

            this.Title = "CAD Importer: " + System.IO.Path.GetFileName(fileName);
        }

        private void FillTreeView(CadAssembly cadAssembly)
        {
            ElementsTreeView.BeginInit();
            ElementsTreeView.Items.Clear();

            foreach (var cadPart in cadAssembly.RootParts)
                FillTreeView(parentTreeViewItem: null, cadPart, cadAssembly.Shells);

            ElementsTreeView.EndInit();
        }

        private void FillTreeView(TreeViewItem? parentTreeViewItem, CadPart cadPart, List<CadShell> allCadShells)
        {
            string? name = cadPart.Name;
            if (string.IsNullOrEmpty(name))
            {
                name = cadPart.Id;
                if (string.IsNullOrEmpty(name))
                {
                    if (parentTreeViewItem != null)
                        name = $"Part_{parentTreeViewItem.Items.Count}";
                    else
                        name = "Part";
                }
            }

            var cadPartItem = new TreeViewItem
            {
                Header = name,
                Tag    = new SelectedPartInfo(cadPart),
            };

            if (parentTreeViewItem == null)
                ElementsTreeView.Items.Add(cadPartItem);
            else
                parentTreeViewItem.Items.Add(cadPartItem);

            if (cadPart.Children != null && cadPart.Children.Count > 0)
            {
                cadPartItem.IsExpanded = true;

                foreach (var childCadPart in cadPart.Children)
                {
                    FillTreeView(cadPartItem, childCadPart, allCadShells);
                }
            }
            else if (cadPart.ShellIndex != -1)
            {
                var cadShell = allCadShells[cadPart.ShellIndex];

                var shellItem = new TreeViewItem
                {
                    Header = "Shell",
                    Tag    = new SelectedPartInfo(cadPart, cadShell),
                    IsExpanded = false
                };

                // Bigger models can have a lot of Faces, so we generate Face items only when user expands a CadPart (when CadShell is shown)
                cadPartItem.Expanded += ShellItemOnExpanded;

                cadPartItem.Items.Add(shellItem);
            }
        }

        private void ShellItemOnExpanded(object sender, RoutedEventArgs e)
        {
            // Bigger models can have a lot of Faces, so we generate Face items only when user expands a CadPart (when CadShell is shown)
            // Here we check if the Face TreeViewItems were already generated (cadShellTreeViewItem.Items.Count > 0) we have the correct data (selectedPartInfo and cadShell)
            if (sender is not TreeViewItem treeViewItem || 
                treeViewItem.Items.Count != 1 ||
                treeViewItem.Items[0] is not TreeViewItem cadShellTreeViewItem ||
                cadShellTreeViewItem.Items.Count > 0 ||
                cadShellTreeViewItem.Tag is not SelectedPartInfo selectedPartInfo ||
                selectedPartInfo.SelectedSubObject is not CadShell cadShell)
            {
                return;
            }

            
            var cadPart = selectedPartInfo.CadPart;

            foreach (var cadFace in cadShell.Faces)
            {
                var faceItem = new TreeViewItem
                {
                    Header = "Face",
                    Tag = new SelectedPartInfo(cadPart, cadFace),
                    IsExpanded = false
                };

                cadShellTreeViewItem.Items.Add(faceItem);


                if (cadFace.EdgeCurves != null && cadFace.EdgeCurves.Length > 0)
                {
                    var wireItem = new TreeViewItem
                    {
                        Header = "Wire",
                        Tag = new SelectedPartInfo(cadPart, cadFace),
                        IsExpanded = false
                    };

                    faceItem.Items.Add(wireItem);

                    for (int i = 0; i < cadFace.EdgeCurves.Length; i++)
                    {
                        var edgeItem = new TreeViewItem
                        {
                            Header = "Edge",
                            Tag = new SelectedPartInfo(cadPart, cadFace, i * 2),
                            IsExpanded = false
                        };

                        wireItem.Items.Add(edgeItem);

                        var cadCurve = cadFace.EdgeCurves[i];
                        if (cadCurve != null)
                            AddCadCurveTreeViewItem(cadCurve, edgeItem);
                    }
                }
            }
        }

        private void AddCadCurveTreeViewItem(CadCurve cadCurve, TreeViewItem parenTreeViewItem)
        {
            string curveTypeName = cadCurve.GetType().Name.Replace("Cad", "");
            
            var edgeItem = new TreeViewItem
            {
                Header = curveTypeName,
                Tag = cadCurve,
                IsExpanded = false
            };

            parenTreeViewItem.Items.Add(edgeItem);
        }

        private void ClearSelection()
        {
            _sceneView.ClearSelection();

            DeselectButton.IsEnabled = false;
            ZoomToObjectButton.IsEnabled = false;
        }

        private void SelectCadObject(TreeViewItem selectedTreeViewItem)
        {
            var selectedPartInfo = selectedTreeViewItem.Tag as SelectedPartInfo;

            if (selectedPartInfo == null)
            {
                if (selectedTreeViewItem.Tag is CadCurve cadCurve)
                {
                    // Select parent Edge
                    if (selectedTreeViewItem.Parent is TreeViewItem parentTreeViewItem)
                        SelectCadObject(parentTreeViewItem);

                    // Now show info about selected curve
                    SelectCadCurve(cadCurve);
                }
                
                return;
            }

            if (selectedPartInfo.SelectedSubObject == null)
            {
                SelectCadPart(selectedPartInfo.CadPart);
            }
            else if (selectedPartInfo.SelectedSubObject is CadShell cadShell)
            {
                SelectCadShell(selectedPartInfo.CadPart, cadShell);
            }
            else if (selectedPartInfo.SelectedSubObject is CadFace cadFace)
            {
                if (selectedPartInfo.EdgeIndex == -1)
                    SelectCadFace(selectedPartInfo.CadPart, cadFace);
                else
                    SelectCadEdge(selectedPartInfo.CadPart, cadFace, selectedPartInfo.EdgeIndex);
            }

            DeselectButton.IsEnabled = true;
            ZoomToObjectButton.IsEnabled = true;
        }

        private void SelectCadCurve(CadCurve cadCurve)
        {
            string? infoText = null;

            switch (cadCurve)
            {
                case CadLine cadLine:
                    infoText = $"Location: ({cadLine.Location.X} {cadLine.Location.Y} {cadLine.Location.Z})\r\nDirection: ({cadLine.Direction.X} {cadLine.Direction.Y} {cadLine.Direction.Z})";
                    break;
                
                case CadCircle cadCircle:
                    infoText = $"Location: ({cadCircle.Location.X} {cadCircle.Location.Y} {cadCircle.Location.Z})\r\nRadius: {cadCircle.Radius}\r\nXAxis: ({cadCircle.XAxis.X} {cadCircle.XAxis.Y} {cadCircle.XAxis.Z})\r\nYAxis: ({cadCircle.YAxis.X} {cadCircle.YAxis.Y} {cadCircle.YAxis.Z})";
                    break;

                case CadEllipse cadEllipse:
                    infoText = $"Location: ({cadEllipse.Location.X} {cadEllipse.Location.Y} {cadEllipse.Location.Z})\r\nMajorRadius: {cadEllipse.MajorRadius}\r\nMinorRadius: {cadEllipse.MinorRadius}\r\nXAxis: ({cadEllipse.XAxis.X} {cadEllipse.XAxis.Y} {cadEllipse.XAxis.Z})\r\nYAxis: ({cadEllipse.YAxis.X} {cadEllipse.YAxis.Y} {cadEllipse.YAxis.Z})";
                    break;
                
                case CadHyperbola cadHyperbola:
                    infoText = $"Location: ({cadHyperbola.Location.X} {cadHyperbola.Location.Y} {cadHyperbola.Location.Z})\r\nMajorRadius: {cadHyperbola.MajorRadius}\r\nMinorRadius: {cadHyperbola.MinorRadius}\r\nXAxis: ({cadHyperbola.XAxis.X} {cadHyperbola.XAxis.Y} {cadHyperbola.XAxis.Z})\r\nYAxis: ({cadHyperbola.YAxis.X} {cadHyperbola.YAxis.Y} {cadHyperbola.YAxis.Z})";
                    break;

                case CadParabola cadParabola:
                    infoText = $"Location: ({cadParabola.Location.X} {cadParabola.Location.Y} {cadParabola.Location.Z})\r\nFocalLength: {cadParabola.FocalLength}\r\nXAxis: ({cadParabola.XAxis.X} {cadParabola.XAxis.Y} {cadParabola.XAxis.Z})\r\nYAxis: ({cadParabola.YAxis.X} {cadParabola.YAxis.Y} {cadParabola.YAxis.Z})";
                    break;

                case CadBezierCurve cadBezierCurve:
                    infoText = $"Poles count: {cadBezierCurve.Poles.Length}\r\nWeights count: {cadBezierCurve.Weights?.Length ?? 0}";
                    break;

                case CadBSplineCurve cadBSplineCurve:
                    infoText = $"Poles count: {cadBSplineCurve.Poles.Length}\r\nWeights count: {cadBSplineCurve.Weights?.Length ?? 0}\r\nKnots count: {cadBSplineCurve.Knots?.Length ?? 0}\r\nDegree: {cadBSplineCurve.Degree}\r\nIsPeriodic: {cadBSplineCurve.IsPeriodic}";
                    break;

                case CadOffsetCurve cadOffsetCurve:
                    infoText = $"BasisCurve: {cadOffsetCurve.BasisCurve.GetType().Name}\r\nOffsetDirection: ({cadOffsetCurve.OffsetDirection.X} {cadOffsetCurve.OffsetDirection.Y} {cadOffsetCurve.OffsetDirection.Z})\r\nOffsetLength: {cadOffsetCurve.OffsetLength}";
                    break;
            }

            if (infoText != null)
            {
                infoText += $"\r\nInterval: [{cadCurve.IntervalStart} {cadCurve.IntervalEnd}]";
            }

            ShowObjectInfo(infoText);
        }

        private void SelectCadEdge(CadPart cadPart, CadFace cadFace, int edgeIndex)
        {
            var selectedEdgePositions = CadAssemblyHelper.GetEdgePositions(cadPart, cadFace, edgeIndex);

            if (selectedEdgePositions == null)
                return;

            ShowSelectedLinePositions(selectedEdgePositions);     
            
            string objectInfoText;
            if (cadFace.EdgeLengths != null)
                objectInfoText = $"EdgeLength: {cadFace.EdgeLengths[edgeIndex / 2]}\r\n";
            else
                objectInfoText = "";

            objectInfoText += $"Positions count: {selectedEdgePositions.Length}";

            ShowObjectInfo(objectInfoText);
        }

        private void SelectCadFace(CadPart cadPart, CadFace cadFace)
        {
            var parentTransformMatrix = CadAssemblyHelper.GetTotalTransformation(cadPart);
            
            _tempEdgeLinePositions.Clear();

            CadAssemblyHelper.AddEdgePositions(cadFace, _tempEdgeLinePositions, ref parentTransformMatrix);

            if (_tempEdgeLinePositions.Count == 0)
                return;

            ShowSelectedLinePositions(_tempEdgeLinePositions.ToArray());   

            string faceInfoText;
            if (cadFace.SurfaceArea > 0)
                faceInfoText = $"SurfaceArea: {cadPart.SurfaceArea}\r\n";
            else
                faceInfoText = "";

            if (cadFace.VertexBuffer != null)
                faceInfoText += $"Positions count: {cadFace.VertexBuffer.Length / 8}\r\n"; // 8 floats for one position (xPos, yPos, zPos, xNormal, yNormal, zNormal, u, v)

            if (cadFace.TriangleIndices != null)
                faceInfoText += $"Triangles count: {cadFace.TriangleIndices.Length / 3}"; // 3 indices for one triangle

            ShowObjectInfo(faceInfoText);
        }

        private void SelectCadShell(CadPart cadPart, CadShell cadShell)
        {
            var parentTransformMatrix = CadAssemblyHelper.GetTotalTransformation(cadPart);

            _tempEdgeLinePositions.Clear();


            int totalTriangles = 0;
            int totalPositions = 0;

            foreach (var cadFace in cadShell.Faces)
            {
                CadAssemblyHelper.AddEdgePositions(cadFace, _tempEdgeLinePositions, ref parentTransformMatrix);

                if (cadFace.TriangleIndices != null)
                    totalTriangles += cadFace.TriangleIndices.Length / 3; // 3 indices for one triangle

                if (cadFace.VertexBuffer != null)
                    totalPositions += cadFace.VertexBuffer.Length / 8; // // 8 floats for one position (xPos, yPos, zPos, xNormal, yNormal, zNormal, u, v)
            }

            if (_tempEdgeLinePositions.Count == 0)
                return;

            ShowSelectedLinePositions(_tempEdgeLinePositions.ToArray());


            string shellInfoText;

            if (!string.IsNullOrEmpty(cadShell.Id))
                shellInfoText = $"Id: {cadShell.Id}\r\n";
            else
                shellInfoText = "";

            shellInfoText += $"Positions count: {totalPositions}\r\nTriangles count: {totalTriangles}";
                
            ShowObjectInfo(shellInfoText);
        }

        private void SelectCadPart(CadPart cadPart)
        {
            if (_cadAssembly == null)
                return;

            var parentTransformMatrix = CadAssemblyHelper.GetTotalTransformation(cadPart.Parent);

            _tempEdgeLinePositions.Clear();

            CadAssemblyHelper.AddEdgePositions(cadPart, _tempEdgeLinePositions, _cadAssembly.Shells, ref parentTransformMatrix);

            if (_tempEdgeLinePositions.Count == 0)
                return;

            ShowSelectedLinePositions(_tempEdgeLinePositions.ToArray());


            string objectInfoText = "";

            if (!string.IsNullOrEmpty(cadPart.Id))
                objectInfoText += $"Id: {cadPart.Id}";

            if (cadPart.Volume > 0 || cadPart.SurfaceArea > 0)
            {
                if (!string.IsNullOrEmpty(objectInfoText))
                    objectInfoText += "\r\n";

                objectInfoText += $"Volume: {cadPart.Volume}\r\nSurfaceArea: {cadPart.SurfaceArea}";
            }

            if (!string.IsNullOrEmpty(objectInfoText))
                objectInfoText += "\r\n";

            objectInfoText += $"X Bounds: min: {cadPart.BoundingBoxMin.X} max: {cadPart.BoundingBoxMax.X}\r\nY Bounds: min: {cadPart.BoundingBoxMin.Y} max: {cadPart.BoundingBoxMax.Y}\r\nZ Bounds: min: {cadPart.BoundingBoxMin.Z} max: {cadPart.BoundingBoxMax.Z}";


            if (cadPart.TransformMatrix != null)
            {
                var partMatrix = CadAssemblyHelper.ReadMatrix(cadPart.TransformMatrix);
                if (!partMatrix.IsIdentity)
                {
                    if (!string.IsNullOrEmpty(objectInfoText))
                        objectInfoText += "\r\n";

                    objectInfoText += "Transformation:\r\n" + _sceneView.GetMatrix3DText(partMatrix);
                }
            }

            ShowObjectInfo(objectInfoText);
        }

        private void ShowObjectInfo(string? objectInfoText)
        {
            if (string.IsNullOrEmpty(objectInfoText))
            {
                SelectedObjectInfoTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                SelectedObjectInfoTextBlock.Visibility = Visibility.Visible;
                SelectedObjectInfoTextBlock.Text = objectInfoText;
            }
        }
        
        
        private void ExpandParentTreeViewItems(TreeViewItem treeViewItem)
        {
            if (treeViewItem.Parent is TreeViewItem parentTreeViewItem)
            {
                parentTreeViewItem.IsExpanded = true;
                ExpandParentTreeViewItems(parentTreeViewItem);
            }
        }

        private TreeViewItem? FindTreeViewItem(CadPart? cadPart, CadFace? cadFace, ItemCollection treeViewItems)
        {
            foreach (var oneItem in treeViewItems)
            {
                if (oneItem is TreeViewItem oneTreeViewItem)
                {
                    if (oneTreeViewItem.Tag is SelectedPartInfo selectedPartInfo)
                    {
                        if (cadPart != null && ReferenceEquals(selectedPartInfo.CadPart, cadPart))
                        {
                            if (cadFace != null)
                                return FindTreeViewItem(cadPart: null, cadFace, oneTreeViewItem.Items);
                            
                            return oneTreeViewItem;
                        }
                        
                        if (cadPart == null && cadFace != null && ReferenceEquals(selectedPartInfo.SelectedSubObject, cadFace))
                        {
                            return oneTreeViewItem;
                        }
                    }

                    if (oneTreeViewItem.Items.Count > 0)
                    {
                        var foundTreeViewItem = FindTreeViewItem(cadPart, cadFace, oneTreeViewItem.Items);
                        if (foundTreeViewItem != null)
                            return foundTreeViewItem;
                    }
                }
            }

            return null;
        }

        private void SceneViewBorderOnMouseMove(object sender, MouseEventArgs e)
        {
            if (_fileName == null)
                return;

            var mousePosition = e.GetPosition(_sceneView);
            UpdateHighlightedPart(mousePosition);
        }

        private void UpdateHighlightedPart(System.Windows.Point mousePosition)
        {
            if (_cadAssembly == null)
                return;

            var selectedPartInfo = _sceneView.GetHitPart(mousePosition);
            if (selectedPartInfo == null)
            {
                ClearHighlightPartOrFace();
                return;
            }


            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Select FACE
                var selectedFace = selectedPartInfo.SelectedSubObject as CadFace;

                if (selectedFace != null)
                    HighlightCadFace(selectedPartInfo.CadPart, selectedFace);
                else
                    ClearHighlightPartOrFace();
            }
            else
            {
                // Select CadPart
                HighlightCadPart(selectedPartInfo.CadPart);
            }
        }

        private void SceneViewBorderOnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_sceneView.IsCameraRotating || _sceneView.IsCameraMoving || _cadAssembly == null || _fileName == null) // Prevent changing the selected part when we stop camera rotation or movement over an object
                return;

            var now = DateTime.Now;
            if ((now - _lastMouseUpTime).TotalSeconds < 0.2) // double-click?
            {
                ZoomToSelectedObject();
                return;
            }

            _lastMouseUpTime = now;

            var mousePosition = e.GetPosition(SceneViewBorder);
            UpdateHighlightedPart(mousePosition);

            if (_highlightedCadPart == null)
            {
                ClearSelection();

                _highlightedCadFace = null;
                _highlightedCadPart = null;

                var selectedTreeViewItem = ElementsTreeView.SelectedItem as TreeViewItem;
                if (selectedTreeViewItem != null)
                    selectedTreeViewItem.IsSelected = false;

                return;
            }


            TreeViewItem? foundTreeViewItem;

            if (_highlightedCadFace != null)
                foundTreeViewItem = FindTreeViewItem(_highlightedCadPart, _highlightedCadFace, ElementsTreeView.Items);
            else
                foundTreeViewItem = FindTreeViewItem(_highlightedCadPart, null, ElementsTreeView.Items);

            if (foundTreeViewItem != null)
            {
                ElementsTreeView.Focus(); // This will show the selected TreeViewItem as blue (instead of as gray)

                ExpandParentTreeViewItems(foundTreeViewItem);

                foundTreeViewItem.IsSelected = true;
                foundTreeViewItem.BringIntoView();
            }
        }


        private double GetSelectedComboBoxDoubleValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem comboBoxItem)
            {
                var selectedText = (string)comboBoxItem.Content;
                if (double.TryParse(selectedText, NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var selectedValue))
                    return selectedValue;
            }

            return 0;
        }

        private void ResetCamera(bool resetCameraRotation = true)
        {
            if (_cadAssembly == null)
                return;

            _sceneView.ResetCamera(_cadAssembly, resetCameraRotation);
        }

        private void DumpLoadedParts(CadAssembly cadAssembly)
        {
            var sb = new StringBuilder();

            int totalPositions = 0;
            int totalTriangles = 0;

            sb.AppendLine("Imported CadAssembly:");
            sb.AppendLine("Shells:");
            for (var i = 0; i < cadAssembly.Shells.Count; i++)
            {
                var oneShell = cadAssembly.Shells[i];
                sb.Append($"[{i}] {oneShell.Id} '{oneShell.Name}' FacesCount: {oneShell.Faces.Count}");

                int shellPositions = 0;
                int shellTriangles = 0;
                
                foreach (var cadFace in oneShell.Faces)
                {
                    shellPositions += cadFace.VertexBuffer.Length / 8;    // 8 floats for one position (xPos, yPos, zPos, xNormal, yNormal, zNormal, u, v)
                    shellTriangles += cadFace.TriangleIndices.Length / 3; // 3 indices for one triangle
                }

                sb.Append($"; Positions count: {shellPositions:#,##0}; Triangles count: {shellTriangles:#,##0}\r\n");

                totalPositions += shellPositions;
                totalTriangles += shellTriangles;
            }

            sb.Append($"\r\nTotal positions count: {totalPositions:#,##0}\r\nTotal triangles count: {totalTriangles:#,##0}\r\n");


            sb.AppendLine("\r\nParts:");

            foreach (var cadPart in cadAssembly.RootParts)
                DumpLoadedParts(cadPart, sb, indent: 0);

            if (InfoTextBox.Text != null && InfoTextBox.Text.Length > 0)
                InfoTextBox.Text += Environment.NewLine + Environment.NewLine;

            InfoTextBox.Text += sb.ToString();
        }

        private void DumpLoadedParts(CadPart cadPart, StringBuilder sb, int indent)
        {
            sb.Append($"{new string(' ', indent)}{cadPart.Id} '{cadPart.Name}' Parent: {(cadPart.Parent == null ? "<null>" : cadPart.Parent.Id)}  ShellIndex: {cadPart.ShellIndex}");

            if (cadPart.TransformMatrix != null)
            {
                var matrix = CadAssemblyHelper.ReadMatrix(cadPart.TransformMatrix);

                if (!matrix.IsIdentity) // Check again for Identity because in IsIdentity on TopLoc_Location in OCCTProxy may not be correct
                {
                    sb.Append(" Transformation:");
                    _sceneView.DumpMatrix(cadPart.TransformMatrix, sb);
                }
            }

            sb.AppendLine();

            if (cadPart.Children != null)
            {
                foreach (var childCadPart in cadPart.Children)
                    DumpLoadedParts(childCadPart, sb, indent + 2);
            }
        }

        private void AddCadImporterLogAction(string message)
        {
            _logStringBuilder?.AppendLine(message);
        }

        private void ShowCadImporterLogMessages()
        {
            if (_logStringBuilder == null)
            {
                InfoTextBox.Text = "";
                return;
            }

            InfoTextBox.Text = _logStringBuilder.ToString();
            InfoTextBox.ScrollToEnd();
        }

        private void HighlightCadPart(CadPart cadPart)
        {
            if (cadPart == _highlightedCadPart)
                return; // Already highlighted

            ClearSelection();
                
            SelectCadPart(cadPart);

            _highlightedCadFace = null;
            _highlightedCadPart = cadPart;
        }
        
        private void HighlightCadFace(CadPart cadPart, CadFace cadFace)
        {
            if (cadFace == _highlightedCadFace)
                return; // Already highlighted

            ClearSelection();

            SelectCadFace(cadPart, cadFace);

            _highlightedCadFace = cadFace;
            _highlightedCadPart = cadPart;
        }

        private void ClearHighlightPartOrFace()
        {
            if (_highlightedCadPart == null && _highlightedCadFace == null)
                return;

            _highlightedCadFace = null;
            _highlightedCadPart = null;

            _sceneView.ClearHighlightPartOrFace();

            if (ElementsTreeView.SelectedItem is TreeViewItem selectedTreeViewItem)
                SelectCadObject(selectedTreeViewItem);
        }


        private void ShowSelectedLinePositions(Vector3[] selectedEdgePositions)
        {
            _sceneView.ShowSelectedLinePositions(selectedEdgePositions);
        }

        private bool ZoomToSelectedObject()
        {
            var selectedTreeViewItem = ElementsTreeView.SelectedItem as TreeViewItem;

            if (selectedTreeViewItem == null)
                return false;

            return _sceneView.ZoomToSelectedObject();
        }

        void TreeViewItemSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ClearSelection();

            var selectedTreeViewItem = e.NewValue as TreeViewItem;

            if (selectedTreeViewItem == null)
                return;

            SelectCadObject(selectedTreeViewItem);
        }

        void TreeViewItemDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            bool iZoomed = ZoomToSelectedObject();
            e.Handled = iZoomed; // prevent closing clicked TreeViewItem
        }

        private void OnLoggingOrDumpingSettingsChanged(object sender, RoutedEventArgs e)
        {
            if ((IsLoggingCheckBox.IsChecked ?? false) || (DumpImportedObjectsCheckBox.IsChecked ?? false))
            {
                if (InfoRow.Height.Value == 0)
                    InfoRow.Height = new GridLength(200, GridUnitType.Pixel);
            }
            else
            {
                InfoRow.Height = new GridLength(0, GridUnitType.Pixel);
            }
        }

        private void DeselectButton_OnClick(object sender, RoutedEventArgs e)
        {
            ClearSelection();
        }

        private void ZoomToObjectButton_OnClick(object sender, RoutedEventArgs e)
        {
            ZoomToSelectedObject();
        }

        private void ShowAllButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetCamera(resetCameraRotation: false);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var now = DateTime.Now;

                if (_lastEscapeKeyPressedTime != DateTime.MinValue && (now - _lastEscapeKeyPressedTime).TotalSeconds < 1)
                {
                    // On second escape key press, we show all objects
                    ResetCamera(resetCameraRotation: false);
                    _lastEscapeKeyPressedTime = DateTime.MinValue;
                }
                else
                {
                    // On first escape key press we deselect selected object
                    ClearSelection();

                    _highlightedCadFace = null;
                    _highlightedCadPart = null;

                    if (ElementsTreeView.SelectedItem is TreeViewItem selectedTreeViewItem)
                        selectedTreeViewItem.IsSelected = false;

                    _lastEscapeKeyPressedTime = now;
                }

                e.Handled = true;
            }
        }

        private void Hyperlink1_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo("https://github.com/Open-Cascade-SAS/OCCT/blob/master/LICENSE_LGPL_21.txt") { UseShellExecute = true });
        }
        
        private void Hyperlink2_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo("https://github.com/Open-Cascade-SAS/OCCT/blob/master/OCCT_LGPL_EXCEPTION.txt") { UseShellExecute = true });
        }

        private void OnShowSolidModelCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            bool isVisible = ShowSolidModelCheckBox.IsChecked ?? false;
            _sceneView.SetSolidModelsVisibility(isVisible);
        }
        
        private void OnEdgeLinesCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            bool isVisible = ShowEdgeLinesCheckBox.IsChecked ?? false;
            _sceneView.SetEdgeLinesVisibility(isVisible);
        }

        private void OnAxisCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            if (ZUpAxisRadioButton.IsChecked ?? false)
                _sceneView.UseZUpAxis();
            else
                _sceneView.UseYUpAxis();
        }
        
        private void OnCameraTypeCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            _sceneView.UsePerspectiveCamera(PerspectiveCameraRadioButton.IsChecked ?? false);
        }

        private void AmbientLightSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!this.IsLoaded)
                return;

            float ambientLightAmount = (float)(AmbientLightSlider.Value / AmbientLightSlider.Maximum);
            _sceneView.SetAmbientLight(ambientLightAmount);
        }

        private void OnCameraLightCheckBoxCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            _sceneView.ShowCameraLight(CameraLightCheckBox.IsChecked ?? false);
        }
        
        private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_fileName == null)
                return;

            LoadCadFile(_fileName);
        }

        private void LoadButton_OnClick(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.InitialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "step_files");

            openFileDialog.Filter = "CAD file (*.step;*.stp;*.iges;*.igs)|*.step;*.stp;*.iges;*.igs";
            openFileDialog.Title = "Select CAD file (STEP or IGES)";

            if ((openFileDialog.ShowDialog() ?? false) && !string.IsNullOrEmpty(openFileDialog.FileName))
                LoadCadFile(openFileDialog.FileName);
        }

        private void OnIsTwoSidedCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            _sceneView.IsTwoSided = IsTwoSidedCheckBox.IsChecked ?? false;
        }

        private void OnViewPanelsVisibilityChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

#if DXENGINE
            _sceneView.IsCameraAxisPanelShown = ShowCameraAxisPanelCheckBox.IsChecked ?? false;
            _sceneView.IsCameraNavigationCirclesShown = ShowCameraNavigationCirclesCheckBox.IsChecked ?? false;
            _sceneView.IsMouseCameraControllerInfoShown = ShowMouseCameraControllerInfoCheckBox.IsChecked ?? false;
#endif
        }
    }
}