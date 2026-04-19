using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UniPlaySong.ViewModels
{
    // One row in the Edit Loops tab of the NSF Track Manager dialog.
    // LoopSecondsInput is two-way bound to a TextBox; empty string means
    // "no override, use default 150s". Validation computes IsValid and
    // ValidationMessage live; the dialog uses IsValid to show/hide the
    // red-border style and to gate the Save command.
    public sealed class NsfLoopRow : INotifyPropertyChanged
    {
        public const int MinLoopSeconds = 5;
        public const int MaxLoopSeconds = 600;

        private string _loopSecondsInput = string.Empty;
        private bool _isPreviewing;

        public int DisplayNumber { get; set; }          // 1-based, for UI
        public string FileName { get; set; }            // displayed in the row
        public string FilePath { get; set; }            // full path, used for preview and saves

        public string LoopSecondsInput
        {
            get { return _loopSecondsInput; }
            set
            {
                _loopSecondsInput = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(HasOverride));
            }
        }

        public bool IsPreviewing
        {
            get { return _isPreviewing; }
            set { _isPreviewing = value; OnPropertyChanged(); }
        }

        // Empty input is valid (means "no override"). Otherwise must parse
        // as an int in [MinLoopSeconds, MaxLoopSeconds].
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_loopSecondsInput)) return true;
                int parsed;
                if (!int.TryParse(_loopSecondsInput.Trim(), out parsed)) return false;
                return parsed >= MinLoopSeconds && parsed <= MaxLoopSeconds;
            }
        }

        public string ValidationMessage
        {
            get
            {
                if (IsValid) return string.Empty;
                return "Must be a whole number between " + MinLoopSeconds + " and " + MaxLoopSeconds + " seconds, or empty to use the default.";
            }
        }

        // True when the row has a valid non-empty override. Used for the
        // "N tracks · M have custom loops" footer counter.
        public bool HasOverride
        {
            get
            {
                if (!IsValid) return false;
                return !string.IsNullOrWhiteSpace(_loopSecondsInput);
            }
        }

        // Returns the parsed loop seconds when HasOverride is true. Caller
        // must check HasOverride first.
        public int LoopSecondsValue
        {
            get { return int.Parse(_loopSecondsInput.Trim()); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
