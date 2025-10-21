using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Ab4d.OpenCascade;
using Ab4d.SharpEngine;
using Ab4d.SharpEngine.Animation;
using Ab4d.SharpEngine.Cameras;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.Meshes;
using Ab4d.SharpEngine.OverlayPanels;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Transformations;
using Ab4d.SharpEngine.Utilities;
using Ab4d.SharpEngine.Wpf;

namespace CadImporter
{       
    /// <summary>
    /// Interaction logic for SharpEngineSceneView.xaml
    /// </summary>
    public partial class SharpEngineSceneView : UserControl
    {
        private LineMaterial? _edgeLineMaterial;
        private LineMaterial? _selectedLineMaterial;
        private LineMaterial? _selectedHiddenLineMaterial;

        private Vector3[]? _selectedEdgeLinePositions;

        
        public bool IsZUpAxis => MainSceneView.Scene.GetCoordinateSystem() == CoordinateSystems.ZUpRightHanded;


        private TargetPositionCamera _targetPositionCamera;

        private PointerCameraController _pointerCameraController;

        private GroupNode _rootGroupNode;
        private GroupNode _selectedLinesGroupNode;
        

        public bool IsTwoSided { get; set; } = true;

        public bool IsCameraRotating { get; private set; }

        public bool IsCameraMoving { get; private set; }


        public event EventHandler? SceneViewInitialized;

        public SharpEngineSceneView()
        {
            InitializeComponent();

            _rootGroupNode = new GroupNode("RootGroupNode");
            _selectedLinesGroupNode = new GroupNode("SelectedLinesGroupNode");

            MainSceneView.Scene.RootNode.Add(_rootGroupNode);
            MainSceneView.Scene.RootNode.Add(_selectedLinesGroupNode);

            _targetPositionCamera = new TargetPositionCamera()
            {
                Heading = -40,
                Attitude = -30,
                Distance = 500,
                ViewWidth = 500,
                TargetPosition = new Vector3(0, 0, 0),
                ShowCameraLight = ShowCameraLightType.Always,
            };

            MainSceneView.SceneView.Camera = _targetPositionCamera;


            _pointerCameraController = new PointerCameraController(MainSceneView)
            {
                RotateCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed,                                                       // this is already the default value but is still set up here for clarity
                MoveCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.ControlKey,               // this is already the default value but is still set up here for clarity
                QuickZoomConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.RightPointerButtonPressed, // quick zoom is disabled by default
                ZoomMode = CameraZoomMode.PointerPosition,
                RotateAroundPointerPosition = true
            };

            _pointerCameraController.CameraRotateStarted += (sender, args) => IsCameraRotating = true;
            _pointerCameraController.CameraRotateEnded += (sender, args) => IsCameraRotating = false;
            _pointerCameraController.CameraMoveStarted += (sender, args) => IsCameraMoving = true;
            _pointerCameraController.CameraMoveEnded += (sender, args) => IsCameraMoving = false;

            var cameraAxisPanel = new CameraAxisPanel(MainSceneView.SceneView, _targetPositionCamera, width: 100, height: 100, adjustSizeByDpiScale: true)
            {
                Position = new Vector2(10, 10),
                Alignment = PositionTypes.BottomLeft
            };
            
            // Wait until SharpEngineSceneView is initialized and then load the step file
            MainSceneView.SceneViewInitialized += (sender, args) =>
            {
                // Before SharpEngine v2.1 we also need to manually set OverlayRenderingLayer.ClearDepthStencilBufferBeforeRendering to true.
                // This will clear the depth-buffer before rendering objects in OverlayRenderingLayer.
                if (MainSceneView.Scene.OverlayRenderingLayer != null)
                    MainSceneView.Scene.OverlayRenderingLayer.ClearDepthStencilBufferBeforeRendering = true;

                OnSceneViewInitialized();
            };

            // In case when VulkanDevice cannot be created, show an error message
            // If this is not handled by the user, then SharpEngineSceneView will show its own error message
            MainSceneView.GpuDeviceCreationFailed += delegate (object sender, DeviceCreateFailedEventArgs args)
            {
                MessageBox.Show("Failed to create Vulkan device:\r\n" + args.Exception.Message);
                args.IsHandled = true; // Prevent showing error by SharpEngineSceneView
            };

            // Cleanup
            this.Unloaded += (sender, args) =>
            {
                MainSceneView.Dispose();
            };
        }

        public void ActivateCadImporter()
        {
            // Ab4d.OpenCascade can be used only when used with the Ab3d.DXEngine or Ab4d.SharpEngine libraries.
            // When Ab4d.OpenCascade is used with Ab3d.DXEngine, it needs to be activated by DXScene (DXScene must be already initialized).
            // When Ab4d.OpenCascade is used with Ab4d.SharpEngine, it needs to be activated by SceneView or Scene (GpuDevice must be already initialized).
            // If you want to use Ab4d.OpenCascade without Ab3d.DXEngine or Ab4d.SharpEngine libraries, contact support (https://www.ab4d.com/Feedback.aspx)
            Ab4d.OpenCascade.CadImporter.Activate(MainSceneView.SceneView);
        }

        public void ClearScene()
        {
            _edgeLineMaterial = null;
            _selectedLineMaterial = null;

            _rootGroupNode.DisposeAllChildren(disposeMeshes: true, disposeMaterials: true, disposeTextures: true);
        }

        public void ProcessCadParts(CadAssembly cadAssembly)
        {
            ProcessCadParts(cadAssembly, cadAssembly.RootParts, _rootGroupNode);
        }

        private void ProcessCadParts(CadAssembly cadAssembly, List<CadPart> cadParts, GroupNode parentGroupNode)
        {
            // All materials are disposed in ClearScene so here we create new materials
            _edgeLineMaterial ??= new LineMaterial()
            {
                LineThickness = 1f,
                LineColor = Color4.Black,
                DepthBias = 0.001f,
            };

            for (var i = 0; i < cadParts.Count; i++)
            {
                var onePart = cadParts[i];

                Transform? transformation;
                if (onePart.TransformMatrix != null)
                {
                    var matrix = CadAssemblyHelper.ReadMatrix(onePart.TransformMatrix);

                    if (!matrix.IsIdentity) // Check again for Identity because in IsIdentity on TopLoc_Location in OCCTProxy may not be correct
                        transformation = new MatrixTransform(matrix);
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
                        var groupNode = new GroupNode($"{onePart.Name}");

                        if (transformation != null)
                            groupNode.Transform = new MatrixTransform(transformation.Value);

                        ProcessCadParts(cadAssembly, onePart.Children, groupNode);

                        if (groupNode.Count > 0) // Add only if it has any children
                            parentGroupNode.Add(groupNode);
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
                            var positionNormalTextures = CadAssemblyHelper.ConvertVertexBufferToPositionNormalTextures(faceData.VertexBuffer);

                            var standardMesh = new StandardMesh(positionNormalTextures, faceData.TriangleIndices, name: $"{onePart.Name}_Face{j}_Mesh");
                            standardMesh.UpdateBoundingBox();

                            Color4 faceColor;
                            if (faceData.FaceColor != null)
                                faceColor = new Color4(faceData.FaceColor[0], faceData.FaceColor[1], faceData.FaceColor[2], faceData.FaceColor[3]);
                            else
                                faceColor = objectColor;


                            var material = new StandardMaterial(faceColor.ToColor3(), opacity: faceColor.Alpha);

                            var objectNode = new MeshModelNode(standardMesh, material, name: $"{onePart.Name}_Face{j}");

                            if (IsTwoSided)
                                objectNode.BackMaterial = material;

                            if (transformation != null)
                                objectNode.Transform = transformation;

                            objectNode.Tag = new SelectedPartInfo(onePart, faceData);
                            
                            parentGroupNode.Add(objectNode);
                        }


                        if (faceData.EdgeIndices != null && faceData.EdgePositionsBuffer != null)
                        {
                            // Get total positions count for an edge
                            var edgePositionsCount = CadAssemblyHelper.GetEdgePositionsCount(faceData);

                            var edgePositions = new Vector3[edgePositionsCount];

                            CadAssemblyHelper.AddEdgePositions(faceData, edgePositions);

                            var edgeLinesNode = new MultiLineNode(edgePositions, isLineStrip: false, _edgeLineMaterial, name: $"{onePart.Name}_Face{j}_EdgeLines");

                            if (transformation != null)
                                edgeLinesNode.Transform = transformation;

                            parentGroupNode.Add(edgeLinesNode);
                        }
                    }
                }
            }
        }

        public void ClearSelection()
        {
            _selectedLinesGroupNode.Clear();
            _selectedEdgeLinePositions = null;
        }

        public SelectedPartInfo? GetHitPart(System.Windows.Point mousePosition)
        {
            var hitTestResult = MainSceneView.SceneView.GetClosestHitObject((float)mousePosition.X, (float)mousePosition.Y);

            if (hitTestResult != null && hitTestResult.HitSceneNode != null && hitTestResult.HitSceneNode.Tag is SelectedPartInfo selectedPartInfo)
                return selectedPartInfo;

            return null;
        }

        public void ResetCamera(CadAssembly cadAssembly, bool resetCameraRotation)
        {
            var cadAssemblyBoundingBox = new BoundingBox(new Vector3((float)cadAssembly.BoundingBoxMin.X, (float)cadAssembly.BoundingBoxMin.Y, (float)cadAssembly.BoundingBoxMin.Z),
                                                         new Vector3((float)cadAssembly.BoundingBoxMax.X, (float)cadAssembly.BoundingBoxMax.Y, (float)cadAssembly.BoundingBoxMax.Z));

            if (resetCameraRotation)
            {
                _targetPositionCamera.Heading = 220;
                _targetPositionCamera.Attitude = -20;
            }

            var boundingBoxLength = cadAssemblyBoundingBox.GetDiagonalLength();

            if (MathUtils.IsZero(boundingBoxLength))
            {
                // When bounding box is not defined, call FitIntoView
                _targetPositionCamera.FitIntoView();
                return;
            }

            _targetPositionCamera.TargetPosition = cadAssemblyBoundingBox.GetCenterPosition();
            _targetPositionCamera.Distance = boundingBoxLength * 2;
            _targetPositionCamera.ViewWidth = boundingBoxLength * 1.5f;
        }

        public void ClearHighlightPartOrFace()
        {
            _selectedLinesGroupNode.Clear();
            _selectedEdgeLinePositions = null;
        }

        public void ShowSelectedLinePositions(Vector3[] selectedEdgePositions)
        {
            _selectedLineMaterial ??= new LineMaterial()
            {
                LineThickness = 1.5f,
                LineColor = Colors.Red,
                // Set DepthBias to prevent rendering wireframe at the same depth as the 3D objects. This creates much nicer 3D lines because lines are rendered on top of 3D object and not in the same position as 3D object.
                // IMPORTANT: The values for selected lines have bigger depth bias than normal edge lines. This renders them on top of normal edge lines.
                DepthBias = 0.002f,
            };
            
            _selectedHiddenLineMaterial ??= new LineMaterial()
            {
                LineThickness = 0.5f,
                LineColor = Colors.Red,
                // Set DepthBias to prevent rendering wireframe at the same depth as the 3D objects. This creates much nicer 3D lines because lines are rendered on top of 3D object and not in the same position as 3D object.
                // IMPORTANT: The values for selected lines have bigger depth bias than normal edge lines. This renders them on top of normal edge lines.
                DepthBias = 0.002f,
            };


            // Ab4d.SharpEngine before v2.1 does not support rendering only hidden lines,
            // so we need to first render always visible lines (line thickness is 0.5).
            // This is done by setting CustomRenderingLayer to Scene.OverlayRenderingLayer.
            // See Advanced/BackgroundAndOverlayRenderingSample sample in the Ab4d.SharpEngine samples project for more info.

            var selectedHiddenLineNode = new MultiLineNode(selectedEdgePositions, isLineStrip: false, material: _selectedHiddenLineMaterial, name: "SelectedHiddenEdgesLineNode");
            
            // To render lines that are always visible, we need to put them to the OverlayRenderingLayer.
            // Before objects from this layer are rendered, the depth buffer is cleared so no previously rendered object will obscure the lines.
            //
            // To do this on ModelNodes or LineNodes, you can use the CustomRenderingLayer property.
            // On GroupNode, you need to call ModelUtils.SetCustomRenderingLayer method to change CustomRenderingLayer property on all child nodes.
            selectedHiddenLineNode.CustomRenderingLayer = MainSceneView.Scene.OverlayRenderingLayer;

            _selectedLinesGroupNode.Add(selectedHiddenLineNode);

            
            // Then render only visible lines on top (line thickness is 1.5).
            var selectedLineNode = new MultiLineNode(selectedEdgePositions, isLineStrip: false, material: _selectedLineMaterial, name: "SelectedEdgesLineNode");
            _selectedLinesGroupNode.Add(selectedLineNode);


            _selectedEdgeLinePositions = selectedEdgePositions;
        }
        
        public void UseZUpAxis()
        {
            if (IsZUpAxis)
                return;

            MainSceneView.Scene.SetCoordinateSystem(CoordinateSystems.ZUpRightHanded);
        }

        public void UseYUpAxis()
        {
            if (!IsZUpAxis)
                return;

            MainSceneView.Scene.SetCoordinateSystem(CoordinateSystems.YUpRightHanded);
        }

        public void UsePerspectiveCamera(bool isPerspectiveCamera)
        {
            _targetPositionCamera.ProjectionType = isPerspectiveCamera ? ProjectionTypes.Perspective : ProjectionTypes.Orthographic;
        }

        public void SetAmbientLight(float ambientLightAmount)
        {
            MainSceneView.Scene.SetAmbientLight(ambientLightAmount);
        }
        
        public void ShowCameraLight(bool showCameraLight)
        {
            _targetPositionCamera.ShowCameraLight = showCameraLight ? ShowCameraLightType.Always : ShowCameraLightType.Never;
        }

        public bool ZoomToSelectedObject()
        {
            if (_selectedEdgeLinePositions == null || _selectedEdgeLinePositions.Length == 0)
                return false;

            var bounds = BoundingBox.FromPoints(_selectedEdgeLinePositions);

            var diagonalLength = bounds.GetDiagonalLength();

            // Animate camera change
            var newTargetPosition = bounds.GetCenterPosition();
            var newDistance = diagonalLength * MathF.Tan(_targetPositionCamera.FieldOfView * MathF.PI / 180.0f) * 2;
            var newCameraWidth = diagonalLength * 1.5f; // for orthographic camera we assume 45 field of view (=> Tan == 1)

            if (IsZUpAxis)
            {
                // When using Z-up coordinate system, we need to swap Y and Z and negate Y
                newTargetPosition = new Vector3(newTargetPosition.X, newTargetPosition.Z, -newTargetPosition.Y);
            }

            var cameraAnimation = AnimationBuilder.CreateCameraAnimation(_targetPositionCamera);

            cameraAnimation.Set(CameraAnimatedProperties.TargetPosition, newTargetPosition, duration: 300);
            cameraAnimation.Set(CameraAnimatedProperties.Distance, newDistance, duration: 300);
            cameraAnimation.Set(CameraAnimatedProperties.ViewWidth, newCameraWidth, duration: 300);

            // Set easing function to all keyframe (we could also set that in each Set method call):
            cameraAnimation.SetEasingFunctionToAllKeyframes(EasingFunctions.CubicEaseInOutFunction);

            cameraAnimation.Start();

            return true;
        }

        public void SetSolidModelsVisibility(bool isVisible)
        {
            var visibility = isVisible ? SceneNodeVisibility.Visible : SceneNodeVisibility.Hidden;
            MainSceneView.Scene.RootNode.ForEachChild<MeshModelNode>(node => node.Visibility = visibility);
        }
        
        public void SetEdgeLinesVisibility(bool isVisible)
        {
            var visibility = isVisible ? SceneNodeVisibility.Visible : SceneNodeVisibility.Hidden;
            MainSceneView.Scene.RootNode.ForEachChild<MultiLineNode>(node => node.Visibility = visibility);
        }

        protected void OnSceneViewInitialized()
        {
            SceneViewInitialized?.Invoke(this, EventArgs.Empty);
        }

        public string GetMatrix3DText(Matrix4x4 matrix)
        {
            return matrix.GetFormattedMatrixText();
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
    }
}