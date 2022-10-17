using Reinterop;
using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CesiumForUnity
{
    public enum IonAssetsColumn
    {
        Name = 0,
        Type = 1,
        DateAdded = 2,
    }

    public class IonAssetDetails
    {
        private string _name;
        private string _type;
        private int _id;
        private string _description;
        private string _attribution;

        public IonAssetDetails(string name, string type, int id, string description, string attribution)
        {
            _name = name;
            _type = type;
            _id = id;
            _description = description;
            _attribution = attribution;
        }

        public string name
        {
            get => _name;
        }

        public string type
        {
            get => _type;
        }

        public int id
        {
            get => _id;
        }

        public string description
        {
            get => _description;
        }

        public string attribution
        {
            get => _attribution;
        }

        private static Dictionary<string, string> typeLookup = new Dictionary<string, string>
        {
            { "3DTILES", "3D Tiles" },
            { "GLTF", "glTF" },
            { "IMAGERY", "Imagery" },
            { "TERRAIN", "Terrain" },
            { "CZML", "CZML" },
            { "KML", "KML" },
            { "GEOJSON", "GeoJSON" }
        };

        public static string FormatType(string type)
        {
            string value;
            if (typeLookup.TryGetValue(type, out value))
            {
                return value;
            }
            return "(Unknown)";
        }

        public static string FormatDate(string assetDate)
        {
            DateTime date = new DateTime();
            bool success = DateTime.TryParse(
                assetDate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out date);

            if (!success)
            {
                Debug.Log("Could not parse date " + assetDate);
            }

            return date.ToString("yyyy-MM-dd");
        }
    }

    [ReinteropNativeImplementation("CesiumForUnityNative::IonAssetsTreeViewImpl", "IonAssetsTreeViewImpl.h")]
    public partial class IonAssetsTreeView : TreeView
    {
        public IonAssetsTreeView(TreeViewState assetsTreeState, MultiColumnHeader header)
            : base(assetsTreeState, header)
        {
            CreateImplementation();
        }

        protected override TreeViewItem BuildRoot()
        {
            int rootId = 0;
            int rootDepth = -1;
            return new TreeViewItem(rootId, rootDepth, "Root");
        }


        public partial int GetAssetsCount();

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            int count = GetAssetsCount();
            IList<TreeViewItem> rows = new List<TreeViewItem>(count);
            // All items are counted as children of the root item, such that when displayed
            // they appear in a list.
            const int itemDepth = 0;

            for (int i = 0; i < count; i++)
            {
                // The root of the tree is typically assigned as 0, so all of the ids
                // have to be offset by 1. Otherwise, the selection behavior of the TreeView
                // may be inaccurate.
                TreeViewItem assetItem = new TreeViewItem(i + 1, itemDepth);
                rows.Insert(i, assetItem);
                root.AddChild(assetItem);
            }

            return rows;
        }

        private partial string GetAssetName(int index);
        private partial string GetAssetType(int index);
        private partial int GetAssetID(int index);
        private partial string GetAssetDescription(int index);
        private partial string GetAssetAttribution(int index);

        public IonAssetDetails GetAssetDetails(int treeId) {
            int index = treeId - 1;
            string name = GetAssetName(index);
            string type = GetAssetType(index);
            int id = GetAssetID(index);
            string description = GetAssetDescription(index);
            string attribution = GetAssetAttribution(index);

            return new IonAssetDetails(name, type, id, description, attribution);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            for (int index = 0; index < args.GetNumVisibleColumns(); ++index)
            {
                int assetIndex = args.item.id - 1;
                CellGUI(args.GetCellRect(index), assetIndex, (IonAssetsColumn)index);
            }
        }

        private partial void CellGUI(Rect cellRect, int assetIndex, IonAssetsColumn column);

        public partial void Refresh();

    }
}

