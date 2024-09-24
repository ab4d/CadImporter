using Ab3d.DirectX;
using Ab3d.Visuals;
using SharpDX;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Ab3d;
using Ab3d.Animation;
using Ab3d.Cameras;
using Ab3d.Common.Cameras;
using Ab3d.Controls;
using Ab3d.DirectX.Materials;
using Ab4d.OpenCascade;

namespace CadImporter
{       
    /// <summary>
    /// Interaction logic for DXEngineSceneView.xaml
    /// </summary>
    public partial class DXEngineSceneView : UserControl
    {
        private DisposeList? _disposables;

        private LineMaterial? _edgeLineMaterial;
        private LineMaterial? _selectedLineMaterial;
        private HiddenLineMaterial? _selectedHiddenLineMaterial;

        private Vector3[]? _selectedEdgeLinePositions;

        private bool _isZUpAxis;
        
        public bool IsTwoSided { get; set; } = true;

        public bool IsCameraAxisPanelShown
        {
            get => CameraAxisPanel1.Visibility == Visibility.Visible;
            set => CameraAxisPanel1.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
        
        public bool IsCameraNavigationCirclesShown
        {
            get => CameraNavigationCircles1.Visibility == Visibility.Visible;
            set => CameraNavigationCircles1.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
        
        public bool IsMouseCameraControllerInfoShown
        {
            get => MouseCameraControllerInfoPanel.Visibility == Visibility.Visible;
            set => MouseCameraControllerInfoPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public bool IsCameraRotating { get; private set; }

        public bool IsCameraMoving { get; private set; }


        public event EventHandler? SceneViewInitialized;

        public DXEngineSceneView()
        {
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


            // Wait until DXEngine is initialized and then load the step file
            MainDXViewportView.DXSceneInitialized += (sender, args) =>
            {
                OnSceneViewInitialized();
            };


            
            // Prevent "childNode is already child of another SceneNode" exception in DXEngine v7.0.8976. This will be fixed with the next version of DXEngine.
            RootModelVisual.Children.Add(new BoxVisual3D());


            // Cleanup
            this.Unloaded += (sender, args) =>
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

        public void ActivateCadImporter()
        {
            // Ab4d.OpenCascade can be used only when used with the Ab3d.DXEngine or Ab4d.SharpEngine libraries.
            // When Ab4d.OpenCascade is used with Ab3d.DXEngine, it needs to be activated by DXScene (DXScene must be already initialized).
            // When Ab4d.OpenCascade is used with Ab4d.SharpEngine, it needs to be activated by SceneView or Scene (GpuDevice must be already initialized).
            // If you want to use Ab4d.OpenCascade without Ab3d.DXEngine or Ab4d.SharpEngine libraries, contact support (https://www.ab4d.com/Feedback.aspx)
            Ab4d.OpenCascade.CadImporter.Activate(MainDXViewportView.DXScene);
        }

        public void ClearScene()
        {
            RootModelVisual.Children.Clear();
        }

        public void ProcessCadParts(CadAssembly cadAssembly)
        {
            if (_disposables != null)
                _disposables.Dispose();

            _disposables = new DisposeList();

            ProcessCadParts(cadAssembly, cadAssembly.RootParts, RootModelVisual);

            MainDXViewportView.Update();
        }

        private void ProcessCadParts(CadAssembly cadAssembly, List<CadPart> cadParts, ModelVisual3D parentVisual3D)
        {
            for (var i = 0; i < cadParts.Count; i++)
            {
                var onePart = cadParts[i];

                Transformation? transformation;
                if (onePart.TransformMatrix != null)
                {
                    SharpDX.Matrix matrix = CadAssemblyHelper.ReadMatrix(onePart.TransformMatrix);

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
                            dxMaterial.IsTwoSided = IsTwoSided;

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
                            var edgePositionsCount = CadAssemblyHelper.GetEdgePositionsCount(faceData);

                            var edgePositions = new Vector3[edgePositionsCount];

                            CadAssemblyHelper.AddEdgePositions(faceData, edgePositions);
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

        public void ClearSelection()
        {
            SelectedLinesModelVisual.Children.Clear();
            _selectedEdgeLinePositions = null;
        }

        public SelectedPartInfo? GetHitPart(System.Windows.Point mousePosition)
        {
            var hitTestResult = MainDXViewportView.GetClosestHitObject(mousePosition);

            if (hitTestResult != null && hitTestResult.HitSceneNode.Tag is SelectedPartInfo selectedPartInfo)
                return selectedPartInfo;

            return null;
        }

        public void ResetCamera(CadAssembly cadAssembly, bool resetCameraRotation)
        {
            var cadAssemblyBoundingBox = new BoundingBox(new Vector3((float)cadAssembly.BoundingBoxMin.X, (float)cadAssembly.BoundingBoxMin.Y, (float)cadAssembly.BoundingBoxMin.Z),
                                                         new Vector3((float)cadAssembly.BoundingBoxMax.X, (float)cadAssembly.BoundingBoxMax.Y, (float)cadAssembly.BoundingBoxMax.Z));

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
            if (_isZUpAxis)
                Camera1.TargetPosition = new Point3D(boundingBoxCenter.X, boundingBoxCenter.Z, -boundingBoxCenter.Y); 
            else
                Camera1.TargetPosition = boundingBoxCenter;
        }

        public void ClearHighlightPartOrFace()
        {
            SelectedLinesModelVisual.Children.Clear();

            _selectedEdgeLinePositions = null;
        }

        public void ShowSelectedLinePositions(Vector3[] selectedEdgePositions)
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

        public void UseZUpAxis()
        {
            if (_isZUpAxis)
                return;

            // When changing coordinate system, we also need to update the Camera's TargetPosition
            var targetPosition = Camera1.TargetPosition;

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

            Camera1.TargetPosition = new Point3D(targetPosition.X, targetPosition.Z, -targetPosition.Y); 

            _isZUpAxis = true;
        }

        public void UseYUpAxis()
        {
            if (!_isZUpAxis)
                return;

            // When changing coordinate system, we also need to update the Camera's TargetPosition
            var targetPosition = Camera1.TargetPosition;

            // WPF and DXEngine use right handed coordinate system (such as OpenGL)

            RootModelVisual.Transform = null;
            SelectedLinesModelVisual.Transform = null;

            CameraNavigationCircles1.UseYUpAxis();

            CameraAxisPanel1.CustomizeAxes(new Vector3D(1, 0, 0), "X", Colors.Red,
                                           new Vector3D(0, 1, 0), "Y", Colors.Blue,
                                           new Vector3D(0, 0, 1), "Z", Colors.Green);

            Camera1.TargetPosition = new Point3D(targetPosition.X, -targetPosition.Z, targetPosition.Y); 

            _isZUpAxis = false;
        }

        public void UsePerspectiveCamera(bool isPerspectiveCamera)
        {
            Camera1.CameraType = isPerspectiveCamera ? BaseCamera.CameraTypes.PerspectiveCamera : BaseCamera.CameraTypes.OrthographicCamera;
        }

        public void SetAmbientLight(float ambientLightAmount)
        {
            var color = (byte)(255 * ambientLightAmount);
            SceneAmbientLight.Color = System.Windows.Media.Color.FromRgb(color, color, color);
        }
        
        public void ShowCameraLight(bool showCameraLight)
        {
            Camera1.ShowCameraLight = showCameraLight ? ShowCameraLightType.Always : ShowCameraLightType.Never;
        }

        public bool ZoomToSelectedObject()
        {
            if (_selectedEdgeLinePositions == null || _selectedEdgeLinePositions.Length == 0)
                return false;

            var bounds = BoundingBox.FromPoints(_selectedEdgeLinePositions);

            var centerPosition = bounds.Center.ToWpfPoint3D();
            var diagonalLength = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height + bounds.Depth * bounds.Depth);


            // Animate camera change
            var newTargetPosition = centerPosition - Camera1.Offset;
            var newDistance = diagonalLength * Math.Tan(Camera1.FieldOfView * Math.PI / 180.0) * 2;
            var newCameraWidth = diagonalLength * 1.5; // for orthographic camera we assume 45 field of view (=> Tan == 1)

            if (_isZUpAxis)
            {
                // When using Z-up coordinate system, we need to swap Y and Z and negate Y
                newTargetPosition = new Point3D(newTargetPosition.X, newTargetPosition.Z, -newTargetPosition.Y); 
            }

            Camera1.AnimateTo(newTargetPosition, newDistance, newCameraWidth, animationDurationInMilliseconds: 300, easingFunction: EasingFunctions.CubicEaseInOutFunction);

            return true;
        }

        public void SetSolidModelsVisibility(bool isVisible)
        {
            Ab3d.Utilities.ModelIterator.IterateModelVisualsObjects(RootModelVisual, (visual3D, transform3D) =>
            {
                if (visual3D is SceneNodeVisual3D sceneNodeVisual && sceneNodeVisual.SceneNode is MeshObjectNode)
                    sceneNodeVisual.IsVisible = isVisible;
            });
        }
        
        public void SetEdgeLinesVisibility(bool isVisible)
        {
            Ab3d.Utilities.ModelIterator.IterateModelVisualsObjects(RootModelVisual, (visual3D, transform3D) =>
            {
                if (visual3D is SceneNodeVisual3D sceneNodeVisual && sceneNodeVisual.SceneNode is ScreenSpaceLineNode)
                    sceneNodeVisual.IsVisible = isVisible;

                //if (visual3D is MultiLineVisual3D multiLineVisual3D)
                //    multiLineVisual3D.IsVisible = isVisible;
            });
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

        protected void OnSceneViewInitialized()
        {
            SceneViewInitialized?.Invoke(this, EventArgs.Empty);
        }

        public string GetMatrix3DText(SharpDX.Matrix matrix)
        {
            return Ab3d.Utilities.Dumper.GetMatrix3DText(matrix.ToWpfMatrix3D());
        }

        public void DumpMatrix(float[] matrixArray, StringBuilder sb)
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

                
        private void MouseCameraController1_OnCameraRotateStarted(object? sender, EventArgs e)
        {
            IsCameraRotating = true;
        }

        private void MouseCameraController1_OnCameraRotateEnded(object? sender, EventArgs e)
        {
            IsCameraRotating = false;
        }

        private void MouseCameraController1_OnCameraMoveStarted(object? sender, EventArgs e)
        {
            IsCameraMoving = true;
        }

        private void MouseCameraController1_OnCameraMoveEnded(object? sender, EventArgs e)
        {
            IsCameraMoving = false;
        }
    }
}