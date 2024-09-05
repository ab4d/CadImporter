using Ab3d.DirectX;
using Ab3d.Visuals;
using SharpDX;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Diagnostics;
using System.Globalization;
using Ab3d.Animation;
using Ab3d.Cameras;
using Ab3d.Common.Cameras;
using Ab3d.Controls;
using Ab3d.DirectX.Materials;
using Ab4d.OpenCascade;

namespace Ab3d.DXEngine.CadImporter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // When the data types returned by the Ab4d.OpenCascade.CadImporter are changed, then the Ab4d.OpenCascade.CadImporter.CadImporterVersion major or minor version is changed (changed build version does not change the data model).
        private static readonly Version SupportedCadImporterVersion = new Version(0, 1);

        private string? _fileName;

        private DisposeList? _disposables;

        private Ab4d.OpenCascade.CadImporter? _cadImporter;
        private CadAssembly? _cadAssembly;

        private StringBuilder? _logStringBuilder;

        private LineMaterial? _edgeLineMaterial;
        private LineMaterial? _selectedLineMaterial;
        private HiddenLineMaterial? _selectedHiddenLineMaterial;

        private Vector3[]? _selectedEdgeLinePositions;
        private List<Vector3> _tempEdgeLinePositions = new List<Vector3>();

        private DateTime _lastEscapeKeyPressedTime;

        private CadPart? _highlightedCadPart;
        private CadFace? _highlightedCadFace;

        private DateTime _lastMouseUpTime;
        private bool _isCameraRotating;
        private bool _isCameraMoving;

        private class SelectedPartInfo
        {
            public CadPart CadPart;
            public object? SelectedSubObject; // This can be CadShell, CadFace, CadEdge or null in case CadPart is selected
            public int EdgeIndex; // This is only valid for CadEdge

            public SelectedPartInfo(CadPart cadPart)
            {
                CadPart = cadPart;
                EdgeIndex = -1;
            }

            public SelectedPartInfo(CadPart cadPart, object? selectedSubObject)
            {
                CadPart = cadPart;
                SelectedSubObject = selectedSubObject;
                EdgeIndex = -1;
            }

            public SelectedPartInfo(CadPart cadPart, object? selectedSubObject, int edgeIndex)
            {
                CadPart = cadPart;
                SelectedSubObject = selectedSubObject;
                EdgeIndex = edgeIndex;
            }
        }

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

            // Update GraphicsProfiles so that the DXEngine first tries to create the UltraQualityHardwareRendering.
            // This uses super-sampling that renders ultra smooth 3D lines.
            MainDXViewportView.GraphicsProfiles = new GraphicsProfile[]
            {
                GraphicsProfile.UltraQualityHardwareRendering,
                GraphicsProfile.HighQualityHardwareRendering,
                GraphicsProfile.NormalQualityHardwareRendering,
                GraphicsProfile.LowQualitySoftwareRendering,
                GraphicsProfile.Wpf3D
            };

            // Add custom mouse info for part / face selection info control
            CameraControllerInfo2.AddCustomInfoLine(0, MouseCameraController.MouseAndKeyboardConditions.LeftMouseButtonPressed, "Select part");
            CameraControllerInfo2.AddCustomInfoLine(1, MouseCameraController.MouseAndKeyboardConditions.ControlKey | MouseCameraController.MouseAndKeyboardConditions.LeftMouseButtonPressed, "Select face");


            _edgeLineMaterial = new LineMaterial()
            {
                LineThickness = 1f,
                LineColor = Color4.Black,
                DepthBias = 0.1f,
                DynamicDepthBiasFactor = 0.02f
            };

            _selectedLineMaterial = new LineMaterial()
            {
                LineThickness = 1.5f,
                LineColor = Colors.Red.ToColor4(),
                // Set DepthBias to prevent rendering wireframe at the same depth as the 3D objects. This creates much nicer 3D lines because lines are rendered on top of 3D object and not in the same position as 3D object.
                // IMPORTANT: The values for selected lines have bigger depth bias than normal edge lines. This renders them on top of normal edge lines.
                DepthBias = 0.15f,
                DynamicDepthBiasFactor = 0.03f
            };

            // Use HiddenLineMaterial instead of LineMaterial to define a line material that renders lines that are behind 3D objects
            _selectedHiddenLineMaterial = new HiddenLineMaterial()
            {
                LineThickness = 0.5f,
                LineColor = Colors.Red.ToColor4(),
                DepthBias = 0.15f,
                DynamicDepthBiasFactor = 0.03f
            };


            // CAD application usually use Z up axis. So set that by default. This can be changed by the user in the Settings.
            UseZUpAxis();


            // Wait until DXEngine is initialized and then load the step file
            MainDXViewportView.DXSceneInitialized += (sender, args) =>
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

            ViewportBorder.MouseMove += ViewportBorderOnMouseMove;
            ViewportBorder.PreviewMouseLeftButtonUp += ViewportBorderOnMouseUp;

            // Check if ESCAPE key is pressed - on first press deselect the object, on second press show all objects
            this.Focusable = true; // by default Page is not focusable and therefore does not receive keyDown event
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Focus();


            // Prevent "childNode is already child of another SceneNode" exception in DXEngine v7.0.8976. This will be fixed with the next version of DXEngine.
            RootModelVisual.Children.Add(new BoxVisual3D());


            // Cleanup
            this.Closing += (sender, args) =>
            {
                if (_selectedHiddenLineMaterial != null)
                {
                    _selectedHiddenLineMaterial.Dispose();
                    _selectedHiddenLineMaterial = null;
                }

                if (_selectedLineMaterial != null)
                {
                    _selectedLineMaterial.Dispose();
                    _selectedLineMaterial = null;
                }

                if (_edgeLineMaterial != null)
                {
                    _edgeLineMaterial.Dispose();
                    _edgeLineMaterial = null;
                }

                _disposables?.Dispose();

                MainDXViewportView.Dispose();
            };
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

            // Ab4d.OpenCascade can be used only when used with the Ab3d.DXEngine or Ab4d.SharpEngine libraries.
            // When Ab4d.OpenCascade is used with Ab3d.DXEngine, it needs to be activated by DXScene (DXScene must be already initialized).
            // When Ab4d.OpenCascade is used with Ab4d.SharpEngine, it needs to be activated by SceneView or Scene (GpuDevice must be already initialized).
            // If you want to use Ab4d.OpenCascade without Ab3d.DXEngine or Ab4d.SharpEngine libraries, contact support (https://www.ab4d.com/Feedback.aspx)
            Ab4d.OpenCascade.CadImporter.Activate(MainDXViewportView.DXScene);

            // Create an instance of CadImporter
            _cadImporter = new Ab4d.OpenCascade.CadImporter();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LoadCadFile(string fileName)
        {
            RootModelVisual.Children.Clear();

            ElementsTreeView.Items.Clear();

            ClearSelection();
            ClearHighlightPartOrFace();

            _fileName = null;
            _cadAssembly = null;

            InfoTextBox.Text = "";

            if (_disposables != null)
                _disposables.Dispose();

            _disposables = new DisposeList();

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


                ProcessCadParts(_cadAssembly, _cadAssembly.RootParts, RootModelVisual);

                MainDXViewportView.Update();

                FillTreeView(_cadAssembly);

                ResetCamera(); // Show all objects


                if (IsLoggingCheckBox.IsChecked ?? false)
                    ShowCadImporterLogMessages();

                if (DumpImportedObjectsCheckBox.IsChecked ?? false)
                    DumpLoadedParts(_cadAssembly);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            
            _fileName = fileName;

            this.Title = "CAD Importer: " + System.IO.Path.GetFileName(fileName);
        }
        
        private void ProcessCadParts(CadAssembly cadAssembly, List<CadPart> cadParts, ModelVisual3D parentVisual3D)
        {
            for (var i = 0; i < cadParts.Count; i++)
            {
                var onePart = cadParts[i];

                Transformation? transformation;
                if (onePart.TransformMatrix != null)
                {
                    SharpDX.Matrix matrix = ReadMatrix(onePart.TransformMatrix);

                    if (!matrix.IsIdentity) // Check again for Identity because in IsIdentity on TopLoc_Location in OCCTProxy may not be correct
                        transformation = new Transformation(matrix);
                    else
                        transformation = null;
                }
                else
                {
                    transformation = null;
                }


                if (onePart.ShellIndex < 0)
                {
                    // NO shell
                    if (onePart.Children != null)
                    {
                        var modelVisual3D = new ModelVisual3D();
                        modelVisual3D.SetName($"{onePart.Name}");

                        if (transformation != null)
                            modelVisual3D.Transform = new MatrixTransform3D(transformation.Value.ToWpfMatrix3D());

                        ProcessCadParts(cadAssembly, onePart.Children, modelVisual3D);

                        if (modelVisual3D.Children.Count > 0) // Add only if it has any children
                            parentVisual3D.Children.Add(modelVisual3D);
                    }
                }
                else
                {
                    var objectColor = new Color4(onePart.PartColor[0], onePart.PartColor[1], onePart.PartColor[2], onePart.PartColor[3]);

                    var oneShell = cadAssembly.Shells[onePart.ShellIndex];

                    for (int j = 0; j < oneShell.Faces.Count; j++)
                    {
                        var faceData = oneShell.Faces[j];

                        if (faceData.VertexBuffer != null && faceData.EdgeIndices != null)
                        {
                            // Convert flat float array to array of PositionNormalTexture
                            var positionNormalTextures = ConvertToPositionNormalTexture(faceData.VertexBuffer);

                            var floatSimpleMesh = new SimpleMesh<PositionNormalTexture>(positionNormalTextures,
                                                                                        faceData.TriangleIndices,
                                                                                        inputLayoutType: InputLayoutType.Position | InputLayoutType.Normal | InputLayoutType.TextureCoordinate,
                                                                                        name: $"{onePart.Name}_Face{j}_Mesh");

                            floatSimpleMesh.CalculateBounds();

                            _disposables?.Add(floatSimpleMesh);

                            Color4 faceColor;

                            if (faceData.FaceColor != null)
                                faceColor = new Color4(faceData.FaceColor[0], faceData.FaceColor[1], faceData.FaceColor[2], faceData.FaceColor[3]);
                            else
                                faceColor = objectColor;


                            var dxMaterial = new Ab3d.DirectX.Materials.StandardMaterial(faceColor.ToColor3(), alpha: faceColor.Alpha);
                            dxMaterial.IsTwoSided = IsTwoSidedCheckBox.IsChecked ?? false;

                            _disposables?.Add(dxMaterial);

                            var objectNode = new Ab3d.DirectX.MeshObjectNode(floatSimpleMesh, dxMaterial, name: $"{onePart.Name}_Face{j}");

                            if (transformation != null)
                                objectNode.Transform = transformation;

                            objectNode.Tag = new SelectedPartInfo(onePart, faceData);

                            _disposables?.Add(objectNode);

                            var sceneNodeVisual3D = new SceneNodeVisual3D(objectNode);
                            sceneNodeVisual3D.SetName($"{onePart.Name}_Face{j}_Visual3D");

                            parentVisual3D.Children.Add(sceneNodeVisual3D);
                        }


                        if (faceData.EdgeIndices != null && faceData.EdgePositionsBuffer != null)
                        {
                            // Get total positions count for an edge
                            var edgePositionsCount = GetEdgePositionsCount(faceData);

                            var edgePositions = new Vector3[edgePositionsCount];

                            AddEdgePositions(faceData, edgePositions);
                            // To transform edge positions call
                            // var matrix = transformation.Value;
                            // AddEdgePositions(faceData, edgePositions, ref matrix);


                            var screenSpaceLineNode = new ScreenSpaceLineNode(edgePositions, isLineStrip: false, isLineClosed: false, _edgeLineMaterial, name: $"{onePart.Name}_Face{j}_EdgeLines");
                            
                            if (transformation != null)
                                screenSpaceLineNode.Transform = transformation;

                            _disposables?.Add(screenSpaceLineNode);

                            var sceneNodeVisual3D = new SceneNodeVisual3D(screenSpaceLineNode);
                            sceneNodeVisual3D.SetName($"{onePart.Name}_Face{j}_EdgeLines_Visual3D");
                            
                            parentVisual3D.Children.Add(sceneNodeVisual3D);


                            // We can also create MultiLineVisual3D from Ab3d.PowerToys library (but using ScreenSpaceLineNode is faster):

                            //var edgePositions = new Point3DCollection(edgePositionsCount);

                            //AddEdgePositions(faceData, edgePositions);

                            //var multiLineVisual3D = new MultiLineVisual3D()
                            //{
                            //    Positions = edgePositions,
                            //    LineColor = Colors.Black,
                            //    LineThickness = 1
                            //};

                            //if (transformation != null)
                            //    multiLineVisual3D.Transform = new MatrixTransform3D(transformation.Value.ToWpfMatrix3D());

                            //multiLineVisual3D.SetName($"{onePart.Name}_Face{j}_EdgeLines");

                            //multiLineVisual3D.SetDXAttribute(DXAttributeType.LineDepthBias, 0.1);
                            //multiLineVisual3D.SetDXAttribute(DXAttributeType.LineDynamicDepthBiasFactor, 0.02);

                            //parentVisual3D.Children.Add(multiLineVisual3D);
                        }
                    }
                }
            }
        }

        private PositionNormalTexture[] ConvertToPositionNormalTexture(float[] vertexBuffer)
        {
            int verticesCount = vertexBuffer.Length / 8;
            var positionNormalTextures = new PositionNormalTexture[verticesCount];

            int vertexIndex = 0;
            for (int i = 0; i < verticesCount; i++)
            {
                int bufferIndex = i * 8;
                positionNormalTextures[vertexIndex].Position          = new Vector3(vertexBuffer[bufferIndex], vertexBuffer[bufferIndex + 1], vertexBuffer[bufferIndex + 2]);
                positionNormalTextures[vertexIndex].Normal            = new Vector3(vertexBuffer[bufferIndex + 3], vertexBuffer[bufferIndex + 4], vertexBuffer[bufferIndex + 5]);
                positionNormalTextures[vertexIndex].TextureCoordinate = new Vector2(vertexBuffer[bufferIndex + 6], vertexBuffer[bufferIndex + 7]);

                vertexIndex++;
            }

            return positionNormalTextures;
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
            SelectedLinesModelVisual.Children.Clear();
            _selectedEdgeLinePositions = null;

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
            if (cadFace.EdgeIndices == null || cadFace.EdgePositionsBuffer == null)
                return;


            var parentTransformMatrix = SharpDX.Matrix.Identity;
            GetTotalTransformation(cadPart, ref parentTransformMatrix);


            // Get total positions count for an edge
            var edgePositionsCount = GetEdgePositionsCount(cadFace);

            var selectedEdgePositions = new Vector3[edgePositionsCount];

            var edgeIndices = cadFace.EdgeIndices;
            var edgePositionsBuffer = cadFace.EdgePositionsBuffer;

            int startIndex = edgeIndices[edgeIndex];
            int indexCount = edgeIndices[edgeIndex + 1];

            int endIndex = startIndex + indexCount * 3 - 3;

            int pos = 0;

            for (var edgePositionIndex = startIndex; edgePositionIndex < endIndex; edgePositionIndex += 3)
            {
                // Add two positions for one line segment

                var onePosition = new Vector3(edgePositionsBuffer[edgePositionIndex],
                                              edgePositionsBuffer[edgePositionIndex + 1],
                                              edgePositionsBuffer[edgePositionIndex + 2]);

                Vector3.Transform(ref onePosition, ref parentTransformMatrix, out onePosition);

                selectedEdgePositions[pos] = onePosition;


                onePosition = new Vector3(edgePositionsBuffer[edgePositionIndex + 3],
                                          edgePositionsBuffer[edgePositionIndex + 4],
                                          edgePositionsBuffer[edgePositionIndex + 5]);

                Vector3.Transform(ref onePosition, ref parentTransformMatrix, out onePosition);

                selectedEdgePositions[pos + 1] = onePosition;

                pos += 2;
            }

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
            var parentTransformMatrix = SharpDX.Matrix.Identity;
            GetTotalTransformation(cadPart, ref parentTransformMatrix);
            
            _tempEdgeLinePositions.Clear();

            AddEdgePositions(cadFace, _tempEdgeLinePositions, ref parentTransformMatrix);

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
            var parentTransformMatrix = SharpDX.Matrix.Identity;
            GetTotalTransformation(cadPart, ref parentTransformMatrix);

            _tempEdgeLinePositions.Clear();


            int totalTriangles = 0;
            int totalPositions = 0;

            foreach (var cadFace in cadShell.Faces)
            {
                AddEdgePositions(cadFace, _tempEdgeLinePositions, ref parentTransformMatrix);

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
            var parentTransformMatrix = SharpDX.Matrix.Identity;
            GetTotalTransformation(cadPart.Parent, ref parentTransformMatrix);

            _tempEdgeLinePositions.Clear();

            AddEdgePositions(cadPart, _tempEdgeLinePositions, ref parentTransformMatrix);

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
                var partMatrix = ReadMatrix(cadPart.TransformMatrix);
                if (!partMatrix.IsIdentity)
                {
                    if (!string.IsNullOrEmpty(objectInfoText))
                        objectInfoText += "\r\n";

                    objectInfoText += "Transformation:\r\n" + Ab3d.Utilities.Dumper.GetMatrix3DText(partMatrix.ToWpfMatrix3D());
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

        private void ViewportBorderOnMouseMove(object sender, MouseEventArgs e)
        {
            if (_fileName == null)
                return;

            var mousePosition = e.GetPosition(MainDXViewportView);
            UpdateHighlightedPart(mousePosition);
        }

        private void UpdateHighlightedPart(System.Windows.Point mousePosition)
        {
            if (_cadAssembly == null)
                return;

            var hitTestResult = MainDXViewportView.GetClosestHitObject(mousePosition);

            if (hitTestResult == null || hitTestResult.HitSceneNode.Tag is not SelectedPartInfo selectedPartInfo)
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

        private void ViewportBorderOnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isCameraRotating || _isCameraMoving || _cadAssembly == null || _fileName == null) // Prevent changing the selected part when we stop camera rotation or movement over an object
                return;

            var now = DateTime.Now;
            if ((now - _lastMouseUpTime).TotalSeconds < 0.2) // double-click?
            {
                ZoomToSelectedObject();
                return;
            }

            _lastMouseUpTime = now;

            var mousePosition = e.GetPosition(MainDXViewportView);
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

            var cadAssemblyBoundingBox = new BoundingBox(new Vector3((float)_cadAssembly.BoundingBoxMin.X, (float)_cadAssembly.BoundingBoxMin.Y, (float)_cadAssembly.BoundingBoxMin.Z),
                                                         new Vector3((float)_cadAssembly.BoundingBoxMax.X, (float)_cadAssembly.BoundingBoxMax.Y, (float)_cadAssembly.BoundingBoxMax.Z));

            if (resetCameraRotation)
            {
                Camera1.Heading = 220;
                Camera1.Attitude = -20;
            }

            Camera1.Offset = new Vector3D(0, 0, 0);


            var boundingBoxLength = cadAssemblyBoundingBox.Size.Length();
            
            if (Ab3d.Utilities.MathUtils.IsZero(boundingBoxLength))
            {
                // When bounding box is not defined, call FitIntoView
                Camera1.FitIntoView();
                return;
            }
            
            Camera1.TargetPosition = cadAssemblyBoundingBox.Center.ToWpfPoint3D();
            Camera1.Distance = boundingBoxLength * 2;
            Camera1.CameraWidth = boundingBoxLength * 1.5;

            // Because cadAssemblyBoundingBox is in Y-up coordinates,
            // we need to swap Y and Z and negate Y when we are using Z-up coordinate system
            var boundingBoxCenter = cadAssemblyBoundingBox.Center.ToWpfPoint3D();
            if (ZUpAxisRadioButton.IsChecked ?? false)
                Camera1.TargetPosition = new Point3D(boundingBoxCenter.X, boundingBoxCenter.Z, -boundingBoxCenter.Y); 
            else
                Camera1.TargetPosition = boundingBoxCenter;
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
                SharpDX.Matrix matrix = ReadMatrix(cadPart.TransformMatrix);

                if (!matrix.IsIdentity) // Check again for Identity because in IsIdentity on TopLoc_Location in OCCTProxy may not be correct
                {
                    sb.Append(" Transformation:");
                    DumpMatrix(cadPart.TransformMatrix, sb);
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

            SelectedLinesModelVisual.Children.Clear();

            _selectedEdgeLinePositions = null;

            if (ElementsTreeView.SelectedItem is TreeViewItem selectedTreeViewItem)
                SelectCadObject(selectedTreeViewItem);
        }


        private void ShowSelectedLinePositions(Vector3[] selectedEdgePositions)
        {
            var screenSpaceLineNode = new ScreenSpaceLineNode(selectedEdgePositions, isLineStrip: false, isLineClosed: false, _selectedLineMaterial, name: "SelectedEdgesLineNode");

            var selectedLinesVisual3D = new SceneNodeVisual3D(screenSpaceLineNode);
            selectedLinesVisual3D.SetName("SelectedEdgesLineVisual3D");

            SelectedLinesModelVisual.Children.Add(selectedLinesVisual3D);
                            
                            
            // Add another line with the same positions but with _selectedHiddenLineMaterial that will show the line when it is hidden behind some 3D object.
            // Note: The hidden line material is derived from HiddenLineMaterial instead of LineMaterial
            var hiddenLinesNode = new ScreenSpaceLineNode(selectedEdgePositions, isLineStrip: false, isLineClosed: false, _selectedHiddenLineMaterial, name: "SelectedHiddenEdgesLineNode");

            var selectedHiddenLinesVisual3D = new SceneNodeVisual3D(hiddenLinesNode);
            selectedHiddenLinesVisual3D.SetName("SelectedHiddenEdgesLineVisual3D");

            SelectedLinesModelVisual.Children.Add(selectedHiddenLinesVisual3D);

            _selectedEdgeLinePositions = selectedEdgePositions;
        }

        private void AddEdgePositions(CadPart cadPart, List<Vector3> edgePositions, ref SharpDX.Matrix parentTransformMatrix)
        {
            SharpDX.Matrix transformMatrix;

            if (cadPart.TransformMatrix != null)
            {
                var tempMatrix = ReadMatrix(cadPart.TransformMatrix);

                if (!tempMatrix.IsIdentity)
                    transformMatrix = tempMatrix * parentTransformMatrix;
                else
                    transformMatrix = parentTransformMatrix;
            }
            else
            {
                transformMatrix = parentTransformMatrix;
            }

            if (cadPart.Children != null)
            {
                foreach (var cadPartChild in cadPart.Children)
                {
                    AddEdgePositions(cadPartChild, edgePositions, ref transformMatrix);
                }
            }

            if (cadPart.ShellIndex != -1 && _cadAssembly != null)
            {
                var cadShell = _cadAssembly.Shells[cadPart.ShellIndex];

                foreach (var cadFace in cadShell.Faces)
                {
                    AddEdgePositions(cadFace, edgePositions, ref transformMatrix);
                }
            }
        }

        private void AddEdgePositions(CadFace cadFace, Vector3[] edgePositions)
        {
            if (cadFace.EdgeIndices == null || cadFace.EdgePositionsBuffer == null)
                return;

            var edgeIndices = cadFace.EdgeIndices;
            var edgePositionsBuffer = cadFace.EdgePositionsBuffer;

            int pos = 0;

            for (var edgeIndex = 0; edgeIndex < edgeIndices.Length; edgeIndex += 2)
            {
                int startIndex = edgeIndices[edgeIndex];
                int indexCount = edgeIndices[edgeIndex + 1];

                int endIndex = startIndex + indexCount * 3 - 3;
                                
                for (var edgePositionIndex = startIndex; edgePositionIndex < endIndex; edgePositionIndex += 3)
                {
                    int actualIndex = edgePositionIndex;

                    // Add two positions for one line segment
                    
                    var onePosition = new Vector3(edgePositionsBuffer[actualIndex],
                                                  edgePositionsBuffer[actualIndex + 1],
                                                  edgePositionsBuffer[actualIndex + 2]);

                    edgePositions[pos] = onePosition;


                    onePosition = new Vector3(edgePositionsBuffer[actualIndex + 3],
                                              edgePositionsBuffer[actualIndex + 4],
                                              edgePositionsBuffer[actualIndex + 5]);

                    edgePositions[pos + 1] = onePosition;

                    pos += 2;
                }
            }
        }
        
        private void AddEdgePositions(CadFace cadFace, List<Vector3> edgePositions, ref SharpDX.Matrix parentTransformMatrix)
        {
            int startPositionIndex = edgePositions.Count;

            AddEdgePositions(cadFace, edgePositions);

            int endPositionIndex = edgePositions.Count;

            for (int i = startPositionIndex; i < endPositionIndex; i++)
            {
                var onePosition = edgePositions[i];
                Vector3.Transform(ref onePosition, ref parentTransformMatrix, out onePosition);
                edgePositions[i] = onePosition;
            }
        }
        
        private void AddEdgePositions(CadFace cadFace, Vector3[] edgePositions, ref SharpDX.Matrix parentTransformMatrix)
        {
            AddEdgePositions(cadFace, edgePositions);

            for (int i = 0; i < edgePositions.Length; i++)
            {
                var onePosition = edgePositions[i];
                Vector3.Transform(ref onePosition, ref parentTransformMatrix, out onePosition);
                edgePositions[i] = onePosition;
            }
        }
        
        private void AddEdgePositions(CadFace cadFace, List<Vector3> edgePositions)
        {
            if (cadFace.EdgeIndices == null || cadFace.EdgePositionsBuffer == null)
                return;

            var edgeIndices = cadFace.EdgeIndices;
            var edgePositionsBuffer = cadFace.EdgePositionsBuffer;

            for (var edgeIndex = 0; edgeIndex < edgeIndices.Length; edgeIndex += 2)
            {
                int startIndex = edgeIndices[edgeIndex];
                int indexCount = edgeIndices[edgeIndex + 1];

                int endIndex = startIndex + indexCount * 3 - 3;
                                
                for (var edgePositionIndex = startIndex; edgePositionIndex < endIndex; edgePositionIndex += 3)
                {
                    int actualIndex = edgePositionIndex;

                    // Add two positions for one line segment
                    
                    var onePosition = new Vector3(edgePositionsBuffer[actualIndex],
                                                  edgePositionsBuffer[actualIndex + 1],
                                                  edgePositionsBuffer[actualIndex + 2]);

                    edgePositions.Add(onePosition);


                    onePosition = new Vector3(edgePositionsBuffer[actualIndex + 3],
                                              edgePositionsBuffer[actualIndex + 4],
                                              edgePositionsBuffer[actualIndex + 5]);

                    edgePositions.Add(onePosition);
                }
            }
        }

        private int GetEdgePositionsCount(CadFace cadFace)
        {
            if (cadFace.EdgeIndices == null)
                return 0;

            var edgeIndices = cadFace.EdgeIndices;

            int totalPositionsCount = 0;

            for (var edgeIndex = 0; edgeIndex < edgeIndices.Length; edgeIndex += 2)
            {
                // First value is startIndex, the second value is indexCount
                //int startIndex = edgeIndices[edgeIndex];
                int indexCount = edgeIndices[edgeIndex + 1];
                int positionsCount = (indexCount - 1) * 2; // each line segment has 2 positions; number of line segments is indexCount - 1
                totalPositionsCount += positionsCount;
            }

            return totalPositionsCount;
        }

        private void GetTotalTransformation(CadPart? cadPart, ref SharpDX.Matrix matrix)
        {
            if (cadPart == null)
                return;

            if (cadPart.TransformMatrix != null)
            {
                var partMatrix = ReadMatrix(cadPart.TransformMatrix);

                if (!partMatrix.IsIdentity)
                    matrix *= partMatrix;
            }

            if (cadPart.Parent != null)
                GetTotalTransformation(cadPart.Parent, ref matrix);
        }
        
        private SharpDX.Matrix ReadMatrix(float[] floatValues)
        {
            var matrix = new SharpDX.Matrix();

            for (int rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < 4; columnIndex++)
                    matrix[columnIndex, rowIndex] = floatValues[(rowIndex * 4) + columnIndex];
            }

            matrix[3, 3] = 1; // We need to manually set the bottom right value to 1

            return matrix;
        }

        private void DumpMatrix(float[] matrixArray, StringBuilder sb)
        {
            // Matrix in OpenCascade is written in column major way - so we reverse the order here to display in row major way
            for (int columnIndex = 0; columnIndex < 4; columnIndex++)
            {
                sb.Append(" [");
                for (int rowIndex = 0; rowIndex < 3; rowIndex++)    
                {
                    sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", matrixArray[(rowIndex * 4) + columnIndex]));
                    if (rowIndex < 2)
                        sb.Append(" ");
                }

                if (columnIndex < 3) // we store only 3 x 4 matrix data - to display the proper 4 x 4 matrix we add 0 and 1
                    sb.Append(" 0");
                else
                    sb.Append(" 1");

                sb.Append("]");
            }
        }

        private void UseZUpAxis()
        {
            // Set the axes so they show left handed coordinate system such Autocad (with z up)
            // Note: WPF and DXEngine use right handed coordinate system (such as OpenGL)

            // The transformation defines the new axis - defined in matrix columns in upper left 3x3 part of the matrix:
            //      x axis - 1st column: 1  0  0  (in the positive x direction - same as WPF 3D) 
            //      y axis - 2nd column: 0  0 -1  (in the negative z direction - into the screen)
            //      z axis - 3rd column: 0  1  0  (in the positive y direction - up)
            var zUpAxisTransformation = new MatrixTransform3D(new Matrix3D(1, 0, 0,  0,
                                                                           0, 0, -1, 0,
                                                                           0, 1, 0,  0,
                                                                           0, 0, 0,  1));

            RootModelVisual.Transform = zUpAxisTransformation;
            SelectedLinesModelVisual.Transform = zUpAxisTransformation;

            CameraNavigationCircles1.UseZUpAxis();

            CameraAxisPanel1.CustomizeAxes(new Vector3D(1, 0, 0), "X", Colors.Red,
                                           new Vector3D(0, 1, 0), "Z", Colors.Blue,
                                           new Vector3D(0, 0, -1), "Y", Colors.Green);
        }

        private void UseYUpAxis()
        {
            // WPF and DXEngine use right handed coordinate system (such as OpenGL)

            RootModelVisual.Transform = null;
            SelectedLinesModelVisual.Transform = null;

            CameraNavigationCircles1.UseYUpAxis();

            CameraAxisPanel1.CustomizeAxes(new Vector3D(1, 0, 0), "X", Colors.Red,
                                           new Vector3D(0, 1, 0), "Y", Colors.Blue,
                                           new Vector3D(0, 0, 1), "Z", Colors.Green);
        }
        
        private bool ZoomToSelectedObject()
        {
            var selectedTreeViewItem = ElementsTreeView.SelectedItem as TreeViewItem;

            if (selectedTreeViewItem == null || _selectedEdgeLinePositions == null || _selectedEdgeLinePositions.Length == 0)
                return false;

            var bounds = BoundingBox.FromPoints(_selectedEdgeLinePositions);

            var centerPosition = bounds.Center.ToWpfPoint3D();
            var diagonalLength = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height + bounds.Depth * bounds.Depth);


            // Animate camera change
            var newTargetPosition = centerPosition - Camera1.Offset;
            var newDistance = diagonalLength * Math.Tan(Camera1.FieldOfView * Math.PI / 180.0) * 2;
            var newCameraWidth = diagonalLength * 1.5; // for orthographic camera we assume 45 field of view (=> Tan == 1)

            if (ZUpAxisRadioButton.IsChecked ?? false)
            {
                // When using Z-up coordinate system, we need to swap Y and Z and negate Y
                newTargetPosition = new Point3D(newTargetPosition.X, newTargetPosition.Z, -newTargetPosition.Y); 
            }

            Camera1.AnimateTo(newTargetPosition, newDistance, newCameraWidth, animationDurationInMilliseconds: 300, easingFunction: EasingFunctions.CubicEaseInOutFunction);

            return true;
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
        
        private void MouseCameraController1_OnCameraRotateStarted(object? sender, EventArgs e)
        {
            _isCameraRotating = true;
        }

        private void MouseCameraController1_OnCameraRotateEnded(object? sender, EventArgs e)
        {
            _isCameraRotating = false;
        }

        private void MouseCameraController1_OnCameraMoveStarted(object? sender, EventArgs e)
        {
            _isCameraMoving = true;
        }

        private void MouseCameraController1_OnCameraMoveEnded(object? sender, EventArgs e)
        {
            _isCameraMoving = false;
        }

        private void OnShowSolidModelCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            bool isVisible = ShowSolidModelCheckBox.IsChecked ?? false;

            Ab3d.Utilities.ModelIterator.IterateModelVisualsObjects(RootModelVisual, (visual3D, transform3D) =>
            {
                if (visual3D is SceneNodeVisual3D sceneNodeVisual && sceneNodeVisual.SceneNode is MeshObjectNode)
                    sceneNodeVisual.IsVisible = isVisible;
            });
        }
        
        private void OnEdgeLinesCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            bool isVisible = ShowEdgeLinesCheckBox.IsChecked ?? false;

            Ab3d.Utilities.ModelIterator.IterateModelVisualsObjects(RootModelVisual, (visual3D, transform3D) =>
            {
                if (visual3D is SceneNodeVisual3D sceneNodeVisual && sceneNodeVisual.SceneNode is ScreenSpaceLineNode)
                    sceneNodeVisual.IsVisible = isVisible;

                //if (visual3D is MultiLineVisual3D multiLineVisual3D)
                //    multiLineVisual3D.IsVisible = isVisible;
            });
        }

        private void OnAxisCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            // When changing coordinate system, we also need to update the Camera's TargetPosition
            var targetPosition = Camera1.TargetPosition;

            if (ZUpAxisRadioButton.IsChecked ?? false)
            {
                UseZUpAxis();
                Camera1.TargetPosition = new Point3D(targetPosition.X, targetPosition.Z, -targetPosition.Y); 
            }
            else
            {
                UseYUpAxis();
                Camera1.TargetPosition = new Point3D(targetPosition.X, -targetPosition.Z, targetPosition.Y); 
            }
        }
        
        private void OnCameraTypeCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            if (PerspectiveCameraRadioButton.IsChecked ?? false)
                Camera1.CameraType = BaseCamera.CameraTypes.PerspectiveCamera;
            else
                Camera1.CameraType = BaseCamera.CameraTypes.OrthographicCamera;
        }

        private void AmbientLightSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!this.IsLoaded)
                return;

            var color = (byte)(2.55 * AmbientLightSlider.Value);
            SceneAmbientLight.Color = System.Windows.Media.Color.FromRgb(color, color, color);
        }

        private void OnCameraLightCheckBoxCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded)
                return;

            if (CameraLightCheckBox.IsChecked ?? false)
                Camera1.ShowCameraLight = ShowCameraLightType.Always;
            else
                Camera1.ShowCameraLight = ShowCameraLightType.Never;
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
    }
}