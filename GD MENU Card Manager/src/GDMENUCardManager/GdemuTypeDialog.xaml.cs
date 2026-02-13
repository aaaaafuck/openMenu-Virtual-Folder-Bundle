using System.ComponentModel;
using System.Windows;

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
            DialogResult = true;
        }

        private void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            IsAuthentic = false;
            _answered = true;
            DialogResult = true;
        }
    }
}
