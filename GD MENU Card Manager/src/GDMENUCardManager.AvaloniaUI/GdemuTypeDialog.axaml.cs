using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GDMENUCardManager
{
    public partial class GdemuTypeDialog : Window
    {
        public bool IsAuthentic { get; private set; }
        private bool _answered;

        public GdemuTypeDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_answered)
                e.Cancel = true;
            base.OnClosing(e);
        }

        private void AuthenticButton_Click(object sender, RoutedEventArgs e)
        {
            IsAuthentic = true;
            _answered = true;
            Close();
        }

        private void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            IsAuthentic = false;
            _answered = true;
            Close();
        }
    }
}
