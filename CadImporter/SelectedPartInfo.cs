using Ab4d.OpenCascade;

namespace CadImporter
{
    public class SelectedPartInfo
    {
        public CadPart CadPart;
        public object? SelectedSubObject; // This can be CadShell, CadFace, CadEdge or null in case CadPart is selected
        public int EdgeIndex;             // This is only valid for CadEdge

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
}