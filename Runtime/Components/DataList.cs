using System;
using System.Collections;
using UnityEngine.UIElements;

namespace Mosaic.UI.Components
{
    [UxmlElement]
    public partial class DataList : VisualElement
    {
        private ListView _listView;
        private VisualTreeAsset _itemTemplate;
        private IList _itemsSource;

        [UxmlAttribute]
        public VisualTreeAsset ItemTemplate
        {
            get => _itemTemplate;
            set
            {
                _itemTemplate = value;
                RebuildListView();
            }
        }

        [UxmlAttribute]
        public SelectionType SelectionType
        {
            get => _listView?.selectionType ?? SelectionType.Single;
            set
            {
                if (_listView != null)
                    _listView.selectionType = value;
            }
        }

        public IList ItemsSource
        {
            get => _itemsSource;
            set
            {
                _itemsSource = value;
                if (_listView != null)
                    _listView.itemsSource = value;
            }
        }

        public int SelectedIndex
        {
            get => _listView?.selectedIndex ?? -1;
            set
            {
                if (_listView != null)
                    _listView.selectedIndex = value;
            }
        }

        public event Action<int> OnSelectionChanged;
        public event Action<int> OnItemClicked;

        public DataList()
        {
            AddToClassList("mosaic-data-list");
            RebuildListView();
        }

        private void RebuildListView()
        {
            if (_listView != null)
            {
                _listView.RemoveFromHierarchy();
            }

            _listView = new ListView();
            _listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _listView.style.flexGrow = 1;

            _listView.makeItem = MakeItem;
            _listView.bindItem = BindItem;

            if (_itemsSource != null)
                _listView.itemsSource = _itemsSource;

            _listView.selectionChanged += _ =>
            {
                OnSelectionChanged?.Invoke(_listView.selectedIndex);
            };

            _listView.itemsChosen += objects =>
            {
                OnItemClicked?.Invoke(_listView.selectedIndex);
            };

            Add(_listView);
        }

        private VisualElement MakeItem()
        {
            if (_itemTemplate != null)
                return _itemTemplate.CloneTree();

            // Fallback: simple label
            var label = new Label();
            label.AddToClassList("mosaic-data-list__item");
            return label;
        }

        private void BindItem(VisualElement element, int index)
        {
            if (_itemsSource == null || index < 0 || index >= _itemsSource.Count)
                return;

            var item = _itemsSource[index];

            // Set the data source on the element for nested data binding
            element.dataSource = item;

            // If no template and using fallback label, set text
            if (_itemTemplate == null && element is Label label)
            {
                label.text = item?.ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// Refreshes the list view to reflect changes in the items source.
        /// Call this after modifying the items source collection.
        /// </summary>
        public void RefreshItems()
        {
            _listView?.RefreshItems();
        }

        /// <summary>
        /// Rebuilds the entire list view. More expensive than RefreshItems.
        /// </summary>
        public void Rebuild()
        {
            _listView?.Rebuild();
        }
    }
}
