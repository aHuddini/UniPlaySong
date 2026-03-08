using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace UniPlaySong.Views
{
    public partial class GenericItemSelectionDialog : Window
    {
        private readonly List<GenericSelectionItem> _items;

        public List<Guid> SelectedIds { get; private set; }

        public GenericItemSelectionDialog(string title, string headerMessage, IEnumerable<(Guid Id, string Name)> allItems, List<Guid> preselectedIds)
        {
            InitializeComponent();
            Title = title;
            HeaderText.Text = headerMessage;

            var selected = preselectedIds ?? new List<Guid>();

            _items = allItems
                .OrderBy(i => i.Name)
                .Select(i => new GenericSelectionItem
                {
                    Id = i.Id,
                    Name = i.Name,
                    IsSelected = selected.Contains(i.Id)
                })
                .ToList();

            foreach (var item in _items)
                item.PropertyChanged += (s, e) => UpdateSelectionCount();

            ItemListBox.ItemsSource = _items;
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            var count = _items.Count(i => i.IsSelected);
            SelectionCountText.Text = $"{count} item(s) selected";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = false;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedIds = _items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class GenericSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid Id { get; set; }
        public string Name { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
