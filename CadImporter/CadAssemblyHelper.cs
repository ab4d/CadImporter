using Ab4d.OpenCascade;

#if DXENGINE
using SharpDX;

// Define alias so that the type names are the same as in Ab4d.SharpEngine.
// To use that only in Ab3d.DXEngine, just replace the "Matrix4x4" with "SharpDX.Matrix"
using Matrix4x4 = SharpDX.Matrix;
using PositionNormalTextureVertex = Ab3d.DirectX.PositionNormalTexture;
#else
using System.Numerics;
using Ab4d.SharpEngine.Common;
#endif

namespace Ab3d.DXEngine.CadImporter;

// This helper class is used to convert generic Ab4d.OpenCascade types that use float to Vector3, PositionNormalTextureVertex and other engine specific types
public static class CadAssemblyHelper
{
#if DXENGINE
    public static SharpDX.Matrix ReadMatrix(float[] floatValues)
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
#else
    public static Matrix4x4 ReadMatrix(float[] floatValues)
    {
        // Read from float array and transpose
        return new Matrix4x4(floatValues[0], floatValues[4], floatValues[8], 0, 
                             floatValues[1], floatValues[5], floatValues[9], 0,
                             floatValues[2], floatValues[6], floatValues[10], 0,
                             floatValues[3], floatValues[7], floatValues[11], 1);
    }
#endif

    public static void AddEdgePositions(CadPart cadPart, List<Vector3> edgePositions, List<CadShell> cadShells, ref Matrix4x4 parentTransformMatrix)
    {
        Matrix4x4 transformMatrix;

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
                AddEdgePositions(cadPartChild, edgePositions, cadShells, ref transformMatrix);
            }
        }

        if (cadPart.ShellIndex != -1)
        {
            var cadShell = cadShells[cadPart.ShellIndex];

            foreach (var cadFace in cadShell.Faces)
            {
                AddEdgePositions(cadFace, edgePositions, ref transformMatrix);
            }
        }
    }

    public static void AddEdgePositions(CadFace cadFace, Vector3[] edgePositions)
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
    
    public static void AddEdgePositions(CadFace cadFace, List<Vector3> edgePositions, ref Matrix4x4 parentTransformMatrix)
    {
        int startPositionIndex = edgePositions.Count;

        AddEdgePositions(cadFace, edgePositions);

        int endPositionIndex = edgePositions.Count;

        for (int i = startPositionIndex; i < endPositionIndex; i++)
        {
            var onePosition = edgePositions[i];
            TransformVector3(ref onePosition, ref parentTransformMatrix, out onePosition);
            edgePositions[i] = onePosition;
        }
    }
    
    public static void AddEdgePositions(CadFace cadFace, Vector3[] edgePositions, ref Matrix4x4 parentTransformMatrix)
    {
        AddEdgePositions(cadFace, edgePositions);

        for (int i = 0; i < edgePositions.Length; i++)
        {
            var onePosition = edgePositions[i];
            TransformVector3(ref onePosition, ref parentTransformMatrix, out onePosition);
            edgePositions[i] = onePosition;
        }
    }
    
    public static void AddEdgePositions(CadFace cadFace, List<Vector3> edgePositions)
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

    public static int GetEdgePositionsCount(CadFace cadFace)
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

    // Note for Ab3d.DXEngine:
    // The PositionNormalTextureVertex type is an alias for Ab3d.DirectX.PositionNormalTexture from Ab3d.DXEngine.
    // The alias is used because Ab4d.SharpEngine defines the PositionNormalTextureVertex type
    public static PositionNormalTextureVertex[] ConvertVertexBufferToPositionNormalTextures(float[] vertexBuffer)
    {
        int verticesCount = vertexBuffer.Length / 8;
        var positionNormalTextures = new PositionNormalTextureVertex[verticesCount];

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


    public static Vector3[]? GetEdgePositions(CadPart cadPart, CadFace cadFace, int edgeIndex)
    {
        if (cadFace.EdgeIndices == null || cadFace.EdgePositionsBuffer == null)
            return null;


        var parentTransformMatrix = Matrix4x4.Identity;
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

            TransformVector3(ref onePosition, ref parentTransformMatrix, out onePosition);

            selectedEdgePositions[pos] = onePosition;


            onePosition = new Vector3(edgePositionsBuffer[edgePositionIndex + 3],
                                      edgePositionsBuffer[edgePositionIndex + 4],
                                      edgePositionsBuffer[edgePositionIndex + 5]);

            TransformVector3(ref onePosition, ref parentTransformMatrix, out onePosition);

            selectedEdgePositions[pos + 1] = onePosition;

            pos += 2;
        }

        return selectedEdgePositions;
    }

    public static Matrix4x4 GetTotalTransformation(CadPart? cadPart)
    {
        var parentTransformMatrix = Matrix4x4.Identity;
        GetTotalTransformation(cadPart, ref parentTransformMatrix);

        return parentTransformMatrix;
    }

    public static void GetTotalTransformation(CadPart? cadPart, ref Matrix4x4 matrix)
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

    public static void TransformVector3(ref Vector3 vector, ref Matrix4x4 transform, out Vector3 result)
    {
#if DXENGINE
        Vector3.Transform(ref vector, ref transform, out result);
#else
        result = Vector3.Transform(vector, transform);
#endif
    }
}