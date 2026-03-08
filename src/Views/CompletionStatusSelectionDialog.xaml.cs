using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Playnite.SDK.Models;

namespace UniPlaySong.Views
{
    public partial class CompletionStatusSelectionDialog : Window
    {
        private readonly List<CompletionStatusItem> _items;

        public List<Guid> SelectedStatusIds { get; private set; }

        public CompletionStatusSelectionDialog(List<CompletionStatus> allStatuses, List<Guid> preselectedIds, Dictionary<Guid, int> songCounts = null)
        {
            InitializeComponent();

            var selected = preselectedIds ?? new List<Guid>();

            _items = allStatuses
                .Select(s => new CompletionStatusItem
                {
                    StatusId = s.Id,
                    StatusName = s.Name,
                    SongCount = songCounts != null && songCounts.TryGetValue(s.Id, out var count) ? count : (int?)null,
                    IsSelected = selected.Contains(s.Id)
                })
                .ToList();

            foreach (var item in _items)
                item.PropertyChanged += (s, e) => UpdateSelectionCount();

            StatusListBox.ItemsSource = _items;
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            var count = _items.Count(i => i.IsSelected);
            SelectionCountText.Text = $"{count} status(es) selected";
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
            SelectedStatusIds = _items
                .Where(i => i.IsSelected)
                .Select(i => i.StatusId)
                .ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class CompletionStatusItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid StatusId { get; set; }
        public string StatusName { get; set; }
        public int? SongCount { get; set; }

        public string DisplayName => SongCount.HasValue
            ? $"{StatusName} ({SongCount} song{(SongCount == 1 ? "" : "s")})"
            : StatusName;

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
