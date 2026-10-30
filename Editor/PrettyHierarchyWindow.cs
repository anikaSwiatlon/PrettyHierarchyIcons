// PrettyHierarchyWindow.cs
#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The UI Toolkit Hierarchy window never raises hierarchyWindowItemOnGUI,
// so rows are decorated through its bind events instead.
public static partial class PrettyHierarchy
{
    private sealed class RowDecoration
    {
        public GameObject obj;
        public bool hasCustomIcon;
        public VisualElement colourBar;
        public Label colourBarLabel;
        public VisualElement iconSlot;
        public Label addIconHint;
    }

    private static readonly ConditionalWeakTable<HierarchyViewItem, RowDecoration> rowDecorations = new();
    private static readonly HashSet<HierarchyViewItem> boundItems = new();

    private static int lastSettingsDirtyCount = -1;
    private static bool openWindowsScanned;

    private static void RegisterHierarchyWindowCallbacks()
    {
        HierarchyWindow.BindViewItem -= OnBindViewItem;
        HierarchyWindow.BindViewItem += OnBindViewItem;

        HierarchyWindow.UnbindViewItem -= OnUnbindViewItem;
        HierarchyWindow.UnbindViewItem += OnUnbindViewItem;

        HierarchyWindow.PopulateContextMenu -= OnPopulateContextMenu;
        HierarchyWindow.PopulateContextMenu += OnPopulateContextMenu;

        EditorApplication.hierarchyChanged -= RefreshBoundItems;
        EditorApplication.hierarchyChanged += RefreshBoundItems;

        Undo.undoRedoPerformed -= RefreshBoundItems;
        Undo.undoRedoPerformed += RefreshBoundItems;

        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnBindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
    {
        boundItems.Add(item);
        DecorateItem(item);
    }

    private static void OnUnbindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
    {
        boundItems.Remove(item);

        if (rowDecorations.TryGetValue(item, out RowDecoration row))
            row.obj = null;
    }

    private static void OnPopulateContextMenu(
        HierarchyWindow window,
        HierarchyView view,
        HierarchyViewItem item,
        DropdownMenu menu)
    {
        if (item == null || settings == null)
            return;

        GameObject obj = GetGameObject(item);

        if (obj == null)
            return;

        menu.AppendSeparator();
        menu.AppendAction("Pretty Hierarchy...", _ => OpenDialog(obj));
    }

    private static void OnEditorUpdate()
    {
        // Rows bound before this domain loaded never raise BindViewItem for us.
        if (!openWindowsScanned)
        {
            openWindowsScanned = true;

            foreach (HierarchyWindow window in Resources.FindObjectsOfTypeAll<HierarchyWindow>())
                window.View?.Query<HierarchyViewItem>().ForEach(item => boundItems.Add(item));

            RefreshBoundItems();
        }

        // Every settings edit calls SetDirty, which bumps the dirty count.
        if (settings == null)
            return;

        int dirtyCount = EditorUtility.GetDirtyCount(settings);

        if (dirtyCount == lastSettingsDirtyCount)
            return;

        lastSettingsDirtyCount = dirtyCount;
        RefreshBoundItems();
    }

    private static void RefreshBoundItems()
    {
        foreach (HierarchyViewItem item in boundItems)
            DecorateItem(item);
    }

    private static GameObject GetGameObject(HierarchyViewItem item)
    {
        Hierarchy source = item.View?.Source;

        if (source == null)
            return null;

        EntityId entityId = source.GetEntityIdFromNode(item.Node);

        return EditorUtility.EntityIdToObject(entityId) as GameObject;
    }

    private static void DecorateItem(HierarchyViewItem item)
    {
        RowDecoration row = GetOrCreateRowDecoration(item);
        GameObject obj = GetGameObject(item);

        row.obj = obj;

        bool active = settings != null && obj != null;

        HierarchyIconSettings.ObjectIconEntry objectEntry = active
            ? settings.GetObjectEntry(obj)
            : null;

        bool showColourBar = objectEntry != null && objectEntry.showColourBar;
        bool showColourIcon = objectEntry != null && objectEntry.showColourIcon;

        Texture2D icon = !active
            ? null
            : objectEntry != null
                ? objectEntry.GetFinalIcon()
                : GetComponentFallbackIcon(obj);

        row.colourBar.style.display = showColourBar ? DisplayStyle.Flex : DisplayStyle.None;

        if (showColourBar)
        {
            row.colourBar.style.backgroundColor = objectEntry.separatorColor;
            row.colourBarLabel.text = obj.name;
        }

        item.Name.style.visibility = showColourBar
            ? new StyleEnum<Visibility>(Visibility.Hidden)
            : new StyleEnum<Visibility>(StyleKeyword.Null);

        row.hasCustomIcon = icon != null || showColourIcon;
        row.iconSlot.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        row.addIconHint.style.display = DisplayStyle.None;

        row.iconSlot.style.backgroundImage = icon != null
            ? new StyleBackground(icon)
            : new StyleBackground(StyleKeyword.None);

        row.iconSlot.style.backgroundColor = showColourIcon
            ? Color.Lerp(
                objectEntry.separatorColor,
                Color.black,
                EditorGUIUtility.isProSkin ? 0.22f : 0.14f)
            : Color.clear;

        // Hide Unity's own icon only while ours is drawn on top of it.
        item.Icon.style.unityBackgroundImageTintColor = row.hasCustomIcon
            ? new StyleColor(Color.clear)
            : new StyleColor(StyleKeyword.Null);
    }

    private static RowDecoration GetOrCreateRowDecoration(HierarchyViewItem item)
    {
        if (rowDecorations.TryGetValue(item, out RowDecoration existing))
            return existing;

        RowDecoration row = new();

        row.colourBar = new VisualElement
        {
            name = "pretty-hierarchy-colour-bar",
            pickingMode = PickingMode.Ignore
        };
        SetAbsoluteFill(row.colourBar);

        row.colourBarLabel = new Label { pickingMode = PickingMode.Ignore };
        SetAbsoluteFill(row.colourBarLabel);
        row.colourBarLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.colourBarLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.colourBarLabel.style.color = Color.white;
        row.colourBar.Add(row.colourBarLabel);

        item.hierarchy.Insert(0, row.colourBar);

        row.iconSlot = new VisualElement { name = "pretty-hierarchy-icon-slot" };
        SetAbsoluteFill(row.iconSlot);
        row.iconSlot.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        row.iconSlot.style.unityBackgroundImageTintColor = EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.78f)
            : new Color(1f, 1f, 1f, 0.88f);

        row.addIconHint = new Label("+") { pickingMode = PickingMode.Ignore };
        SetAbsoluteFill(row.addIconHint);
        row.addIconHint.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.addIconHint.style.color = new Color(1f, 1f, 1f, 0.35f);
        row.iconSlot.Add(row.addIconHint);

        row.iconSlot.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (row.obj != null && !row.hasCustomIcon)
                row.addIconHint.style.display = DisplayStyle.Flex;
        });

        row.iconSlot.RegisterCallback<PointerLeaveEvent>(_ =>
            row.addIconHint.style.display = DisplayStyle.None);

        row.iconSlot.RegisterCallback<PointerDownEvent>(evt => OnIconSlotPointerDown(row, evt));

        // Keep the row's own context menu from opening on top of the dialog.
        row.iconSlot.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 1 && row.obj != null)
                evt.StopImmediatePropagation();
        });

        item.Icon.hierarchy.Add(row.iconSlot);

        rowDecorations.Add(item, row);
        return row;
    }

    private static void OnIconSlotPointerDown(RowDecoration row, PointerDownEvent evt)
    {
        if (row.obj == null || settings == null)
            return;

        if (evt.button == 1)
        {
            Selection.activeGameObject = row.obj;
            OpenDialog(row.obj);
            evt.StopImmediatePropagation();
        }
        else if (evt.button == 0 && evt.clickCount == 2)
        {
            OpenScript(GetFirstNonUnityScript(row.obj));
            evt.StopImmediatePropagation();
        }
    }

    private static void OpenDialog(GameObject obj)
    {
        PrettyHierarchyDialog.Open(settings, obj, GetFirstNonUnityScript(obj));
    }

    private static void SetAbsoluteFill(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = 0f;
        element.style.right = 0f;
        element.style.top = 0f;
        element.style.bottom = 0f;
    }
}
#endif
