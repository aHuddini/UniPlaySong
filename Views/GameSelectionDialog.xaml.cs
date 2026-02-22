using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Playnite.SDK.Models;

namespace UniPlaySong.Views
{
    public partial class GameSelectionDialog : Window
    {
        private readonly List<GameSelectionItem> _allItems;

        public List<Guid> SelectedGameIds { get; private set; }

        public GameSelectionDialog(List<Game> gamesWithMusic, List<Guid> preselectedIds)
        {
            InitializeComponent();

            var selected = preselectedIds ?? new List<Guid>();

            _allItems = gamesWithMusic
                .Select(g => new GameSelectionItem
                {
                    GameId = g.Id,
                    GameName = g.Name,
                    IsSelected = selected.Contains(g.Id)
                })
                .ToList();

            // Subscribe to selection changes for count updates
            foreach (var item in _allItems)
                item.PropertyChanged += (s, e) => UpdateSelectionCount();

            GameListBox.ItemsSource = _allItems;
            UpdateSelectionCount();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var filter = SearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(filter))
            {
                GameListBox.ItemsSource = _allItems;
            }
            else
            {
                GameListBox.ItemsSource = _allItems
                    .Where(i => i.GameName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
        }

        private void UpdateSelectionCount()
        {
            var count = _allItems.Count(i => i.IsSelected);
            SelectionCountText.Text = $"{count} game(s) selected";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            // Only select visible (filtered) items
            var visible = GameListBox.ItemsSource as IEnumerable<GameSelectionItem> ?? _allItems;
            foreach (var item in visible)
                item.IsSelected = true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allItems)
                item.IsSelected = false;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedGameIds = _allItems
                .Where(i => i.IsSelected)
                .Select(i => i.GameId)
                .ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class GameSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid GameId { get; set; }
        public string GameName { get; set; }

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
