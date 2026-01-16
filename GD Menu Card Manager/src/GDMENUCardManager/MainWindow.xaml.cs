using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GDMENUCardManager.Core;
using GongSolutions.Wpf.DragDrop;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget, INotifyPropertyChanged
    {
        private Core.Manager _ManagerInstance;
        public Core.Manager Manager { get { return _ManagerInstance; } }

        private readonly bool showAllDrives = false;
        private string _originalFolderValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DriveInfo> DriveList { get; } = new ObservableCollection<DriveInfo>();



        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { _IsBusy = value; RaisePropertyChanged(); }
        }

        private DriveInfo _DriveInfo;
        public DriveInfo SelectedDrive
        {
            get { return _DriveInfo; }
            set
            {
                _DriveInfo = value;
                Manager.ItemList.Clear();
                Manager.sdPath = value?.RootDirectory.ToString();
                Filter = null;
                RaisePropertyChanged();
            }
        }

        private string _TempFolder;
        public string TempFolder
        {
            get { return _TempFolder; }
            set { _TempFolder = value; RaisePropertyChanged(); }
        }

        private string _TotalFilesLength;
        public string TotalFilesLength
        {
            get { return _TotalFilesLength; }
            private set { _TotalFilesLength = value; RaisePropertyChanged(); }
        }

        private bool _HaveGDIShrinkBlacklist;
        public bool HaveGDIShrinkBlacklist
        {
            get { return _HaveGDIShrinkBlacklist; }
            set { _HaveGDIShrinkBlacklist = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrink;
        public bool EnableGDIShrink
        {
            get { return Manager.EnableGDIShrink; }
            set { Manager.EnableGDIShrink = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrinkCompressed;
        public bool EnableGDIShrinkCompressed
        {
            get { return Manager.EnableGDIShrinkCompressed; }
            set { Manager.EnableGDIShrinkCompressed = value; RaisePropertyChanged(); }
        }

        //private bool _EnableGDIShrinkBlackList = true;
        public bool EnableGDIShrinkBlackList
        {
            get { return Manager.EnableGDIShrinkBlackList; }
            set { Manager.EnableGDIShrinkBlackList = value; RaisePropertyChanged(); }
        }

        public MenuKind MenuKindSelected
        {
            get { return Manager.MenuKindSelected; }
            set
            {
                Manager.MenuKindSelected = value;
                RaisePropertyChanged();
                UpdateFolderColumnVisibility();
            }
        }

        private string _Filter;
        public string Filter
        {
            get { return _Filter; }
            set { _Filter = value; RaisePropertyChanged(); }
        }

        private readonly string fileFilterList;

        public MainWindow()
        {
            InitializeComponent();

            var compressedFileFormats = new string[] { ".7z", ".rar", ".zip" };
            _ManagerInstance = Core.Manager.CreateInstance(new DependencyManager(), compressedFileFormats);
            var fullList = Manager.supportedImageFormats.Concat(compressedFileFormats).Select(x => $"*{x}").ToArray();
            fileFilterList = $"Dreamcast Game ({string.Join("; ", fullList)})|{string.Join(';', fullList)}";

            this.Loaded += (ss, ee) =>
            {
                HaveGDIShrinkBlacklist = File.Exists(Constants.GdiShrinkBlacklistFile);
                FillDriveList();
                // Defer column visibility update until DataGrid is fully loaded
                Dispatcher.BeginInvoke(new Action(() => UpdateFolderColumnVisibility()), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            this.Closing += MainWindow_Closing;
            this.PropertyChanged += MainWindow_PropertyChanged;
            Manager.ItemList.CollectionChanged += ItemList_CollectionChanged;
            Manager.MenuKindChanged += Manager_MenuKindChanged;

            SevenZip.SevenZipExtractor.SetLibraryPath(Environment.Is64BitProcess ? "7z64.dll" : "7z.dll");

            //config parsing. all settings are optional and must reverse to default values if missing
            bool.TryParse(ConfigurationManager.AppSettings["ShowAllDrives"], out showAllDrives);
            bool.TryParse(ConfigurationManager.AppSettings["Debug"], out Manager.debugEnabled);
            if (bool.TryParse(ConfigurationManager.AppSettings["UseBinaryString"], out bool useBinaryString))
                Converter.ByteSizeToStringConverter.UseBinaryString = useBinaryString;
            if (int.TryParse(ConfigurationManager.AppSettings["CharLimit"], out int charLimit))
                GdItem.namemaxlen = Math.Min(256, Math.Max(charLimit, 1));
            if (int.TryParse(ConfigurationManager.AppSettings["ProductIdMaxLength"], out int productIdMaxLength))
                GdItem.serialmaxlen = Math.Min(32, Math.Max(productIdMaxLength, 1));
            if (bool.TryParse(ConfigurationManager.AppSettings["TruncateMenuGDI"], out bool truncateMenuGDI))
                Manager.TruncateMenuGDI = truncateMenuGDI;

            TempFolder = Path.GetTempPath();
            Title = "GD MENU Card Manager " + Constants.Version;

            //showAllDrives = true;

            DataContext = this;
        }

        private async void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedDrive) && SelectedDrive != null)
                await LoadItemsFromCard();
            else if (e.PropertyName == nameof(MenuKindSelected))
                UpdateFolderColumnVisibility();
        }

        private void Manager_MenuKindChanged(object sender, EventArgs e)
        {
            // Update column visibility immediately when menu kind is detected during loading
            Dispatcher.Invoke(new Action(() =>
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                UpdateFolderColumnVisibility();
            }));
        }

        private void UpdateFolderColumnVisibility()
        {
            if (dg1?.Columns == null)
                return;

            // Find columns by iterating and checking their Header
            DataGridColumn folderColumn = null;
            DataGridColumn typeColumn = null;
            DataGridTextColumn discColumn = null;

            foreach (var col in dg1.Columns)
            {
                if (col.Header?.ToString() == "Folder")
                    folderColumn = col;
                else if (col is DataGridTemplateColumn templateCol && templateCol.Header?.ToString() == "Type")
                    typeColumn = col;
                else if (col is DataGridTextColumn discTextCol && discTextCol.Header?.ToString() == "Disc")
                    discColumn = discTextCol;
            }

            if (folderColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    folderColumn.Visibility = Visibility.Visible;
                    folderColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                else
                {
                    folderColumn.Visibility = Visibility.Collapsed;
                }
            }

            if (typeColumn != null)
            {
                if (MenuKindSelected == MenuKind.openMenu)
                {
                    typeColumn.Visibility = Visibility.Visible;
                }
                else
                {
                    typeColumn.Visibility = Visibility.Collapsed;
                }
            }

            if (discColumn != null)
            {
                // Make Disc column editable only in openMenu mode
                discColumn.IsReadOnly = (MenuKindSelected != MenuKind.openMenu);
            }
        }

        private void ItemList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            updateTotalSize();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsBusy)
                e.Cancel = true;
            else
                Manager.ItemList.CollectionChanged -= ItemList_CollectionChanged;//release events
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void updateTotalSize()
        {
            var bsize = ByteSizeLib.ByteSize.FromBytes(Manager.ItemList.Sum(x => x.Length.Bytes));
            TotalFilesLength = Converter.ByteSizeToStringConverter.UseBinaryString ? bsize.ToBinaryString() : bsize.ToString();
        }


        private async Task LoadItemsFromCard()
        {
            IsBusy = true;

            try
            {
                await Manager.LoadItemsFromCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Problem loading the following folder(s):\n\n{ex.Message}", "Invalid Folders", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                RaisePropertyChanged(nameof(MenuKindSelected));
                IsBusy = false;
            }
        }

        private async Task Save()
        {
            IsBusy = true;
            try
            {
                // Check for multi-disc items without serial (openMenu only)
                if (MenuKindSelected == MenuKind.openMenu && HasMultiDiscItemsWithoutSerial())
                {
                    var dialog = new WarningDialog(
                        "One or more disc images that are part of multi-disc sets do not have a required Serial value assigned to them, which will break their display in openMenu.\n\nDo you want to proceed or return to make edits?");
                    dialog.Owner = this;

                    if (dialog.ShowDialog() != true || !dialog.Proceed)
                    {
                        IsBusy = false;
                        return;
                    }
                }

                // Check for multi-disc sets exceeding 10 discs (openMenu only)
                if (MenuKindSelected == MenuKind.openMenu && HasMultiDiscSetsExceeding10())
                {
                    var dialog = new WarningDialog(
                        "One or more multi-disc set exceeds 10 discs total, the maximum supported by openMenu.\n\nDo you want to proceed or return to make edits?");
                    dialog.Owner = this;

                    if (dialog.ShowDialog() != true || !dialog.Proceed)
                    {
                        IsBusy = false;
                        return;
                    }
                }

                if (await Manager.Save(TempFolder))
                    MessageBox.Show(this, "Done!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                updateTotalSize();
            }
        }

        private bool HasMultiDiscItemsWithoutSerial()
        {
            return Manager.ItemList.Any(item =>
            {
                // Skip menu items
                if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                    return false;

                if (string.IsNullOrWhiteSpace(item.ProductNumber))
                {
                    var disc = item.Ip?.Disc;
                    if (!string.IsNullOrEmpty(disc))
                    {
                        var parts = disc.Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[1], out int totalDiscs) &&
                            totalDiscs > 1)
                        {
                            return true;
                        }
                    }
                }
                return false;
            });
        }

        private bool HasMultiDiscSetsExceeding10()
        {
            return Manager.ItemList.Any(item =>
            {
                // Skip menu items
                if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                    return false;

                var disc = item.Ip?.Disc;
                if (!string.IsNullOrEmpty(disc))
                {
                    var parts = disc.Split('/');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[1], out int totalDiscs) &&
                        totalDiscs > 10)
                    {
                        return true;
                    }
                }
                return false;
            });
        }


        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            DragDropHandler.DragOver(dropInfo);
        }

        async void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (dropInfo == null)
                return;

            IsBusy = true;
            try
            {
                await DragDropHandler.Drop(dropInfo);
            }
            catch (InvalidDropException ex)
            {
                var w = new TextWindow("Ignored folders/files", ex.Message);
                w.Owner = this;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        

        private async void ButtonSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            await Save();
        }

        private void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            new AboutWindow { Owner = this }.ShowDialog();
            IsBusy = false;
        }

        private void ButtonFolder_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if ((string)btn.CommandParameter == nameof(TempFolder) && !string.IsNullOrEmpty(TempFolder))
                    dialog.SelectedPath = TempFolder;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TempFolder = dialog.SelectedPath;
            }
        }

        //private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        //{
        //    var grid = sender as DataGridRow;
        //    GdItem model;
        //    if (grid != null && grid.DataContext != null && (model = grid.DataContext as GdItem) != null)
        //    {
        //        IsBusy = true;

        //        var helptext = $"{model.Ip.Name}\n{model.Ip.Version}\n{model.Ip.Disc}";

        //        MessageBox.Show(helptext, "IP.BIN Info", MessageBoxButton.OK, MessageBoxImage.Information);
        //        IsBusy = false;
        //    }
        //}

        private async void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                var btn = (Button)sender;
                var item = (GdItem)btn.CommandParameter;

                if (item.Ip == null)
                    await Manager.LoadIP(item);

                new InfoWindow(item) { Owner = this}.ShowDialog();
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Loading data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private async void ButtonSort_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            try
            {
                await Manager.SortList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Loading data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private async void ButtonBatchRename_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                var w = new CopyNameWindow();
                w.Owner = this;

                if (!w.ShowDialog().GetValueOrDefault())
                    return;

                var count = await Manager.BatchRenameItems(w.NotOnCard, w.OnCard, w.FolderName, w.ParseTosec);

                MessageBox.Show($"{count} item(s) renamed", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonBatchFolderRename_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            try
            {
                var folderCounts = Manager.GetFolderCounts();

                if (folderCounts.Count == 0)
                {
                    MessageBox.Show("No folders found in the current game list.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var window = new BatchFolderRenameWindow(folderCounts, Manager.ItemList.Count);
                window.Owner = this;

                if (window.ShowDialog() == true && window.FolderMappings != null)
                {
                    var updatedCount = Manager.ApplyFolderMappings(window.FolderMappings);

                    if (updatedCount > 0)
                    {
                        MessageBox.Show($"{updatedCount} disc image(s) updated across {window.FolderMappings.Count} folder(s).\n\nClick 'Save Changes' to write updates to SD card.",
                                        "Folders Renamed", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No changes were made.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ButtonPreload_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0)
                return;

            IsBusy = true;
            try
            {
                await Manager.LoadIpAll();
            }
            catch (ProgressWindowClosedException) { }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonRefreshDrive_Click(object sender, RoutedEventArgs e)
        {
            FillDriveList(true);
        }

        private void FillDriveList(bool isRefreshing = false)
        {
            var list = DriveInfo.GetDrives().Where(x => x.IsReady && (showAllDrives || (x.DriveType == DriveType.Removable && x.DriveFormat.StartsWith("FAT")))).ToArray();

            if (isRefreshing)
            {
                if (DriveList.Select(x => x.Name).SequenceEqual(list.Select(x => x.Name)))
                    return;

                DriveList.Clear();
            }
            //fill drive list and try to find drive with gdemu contents
            foreach (DriveInfo drive in list)
            {
                DriveList.Add(drive);
                //look for GDEMU.ini file
                if (SelectedDrive == null && File.Exists(Path.Combine(drive.RootDirectory.FullName, Constants.MenuConfigTextFile)))
                    SelectedDrive = drive;
            }

            //look for 01 folder
            if (SelectedDrive == null)
            {
                foreach (DriveInfo drive in list)
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "01")))
                    {
                        SelectedDrive = drive;
                        break;
                    }
            }


            if (!DriveList.Any())
                return;

            if (SelectedDrive == null)
                SelectedDrive = DriveList.LastOrDefault();
        }

        private void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            dg1.BeginEdit();
        }

        private void MenuItemRenameSentence_Click(object sender, RoutedEventArgs e)
        {
            TextInfo textInfo = new CultureInfo("en-US",false).TextInfo;

            dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
            IEnumerable<GdItem> items = dg1.SelectedItems.Cast<GdItem>();

            foreach (var item in items)
            {
                item.Name = textInfo.ToTitleCase( textInfo.ToLower( item.Name) );
            }
        }

        private async void MenuItemRenameIP_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Ip);
        }
        private async void MenuItemRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.Folder);
        }
        private async void MenuItemRenameFile_Click(object sender, RoutedEventArgs e)
        {
            await renameSelection(RenameBy.File);
        }

        private async Task renameSelection(RenameBy renameBy)
        {
            IsBusy = true;
            try
            {
                await Manager.RenameItems(dg1.SelectedItems.Cast<GdItem>(), renameBy);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsBusy = false;
        }

        private void MenuItemAssignFolder_Click(object sender, RoutedEventArgs e)
        {
            // Only allow in openMenu mode
            if (MenuKindSelected != MenuKind.openMenu)
            {
                MessageBox.Show("Assign Folder Path is only available in openMenu mode.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItems = dg1.SelectedItems.Cast<GdItem>().ToList();

            // Filter out menu items
            selectedItems = selectedItems.Where(item =>
                item.Ip?.Name != "GDMENU" && item.Ip?.Name != "openMenu").ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No valid items selected.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Manager.InitializeKnownFolders();
            var dialog = new AssignFolderWindow(selectedItems.Count, Manager.KnownFolders);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                var folderPath = dialog.FolderPath?.Trim() ?? string.Empty;
                foreach (var item in selectedItems)
                {
                    item.Folder = folderPath;
                }
            }
        }

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            // Check if this is a menu item
            if (e.Row?.DataContext is GdItem item)
            {
                bool isMenuItem = item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu";

                if (isMenuItem)
                {
                    // Prevent editing ANY cell for menu items
                    e.Cancel = true;
                }
            }
        }

        private async void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && !(e.OriginalSource is TextBox))
            {
                dg1.CurrentCell = new DataGridCellInfo(dg1.SelectedItem, dg1.Columns[4]);
                dg1.BeginEdit();
            }
            else if (e.Key == Key.Delete && !(e.OriginalSource is TextBox))
            {
                var grid = (DataGrid)sender;
                List<GdItem> toRemove = new List<GdItem>();
                foreach (GdItem item in grid.SelectedItems)
                {
                    if (item.SdNumber == 1)
                    {
                        if (item.Ip == null)
                        {
                            IsBusy = true;
                            await Manager.LoadIP(item);
                            IsBusy = false;
                        }
                        if (item.Ip.Name != "GDMENU" && item.Ip.Name != "openMenu")//dont let the user exclude GDMENU
                            toRemove.Add(item);
                    }
                    else
                    {
                        toRemove.Add(item);
                    }
                }

                foreach (var item in toRemove)
                    Manager.ItemList.Remove(item);

                e.Handled = true;
            }
        }

        private async void ButtonAddGames_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Filter = fileFilterList;
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    IsBusy = true;

                    var invalid = await Manager.AddGames(dialog.FileNames);

                    if (invalid.Any())
                    {
                        var w = new TextWindow("Ignored folders/files", string.Join(Environment.NewLine, invalid));
                        w.Owner = this;
                        w.ShowDialog();
                    }
                    IsBusy = false;
                }
            }
        }

        private void ButtonRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            while (dg1.SelectedItems.Count > 0)
                Manager.ItemList.Remove((GdItem)dg1.SelectedItems[0]);
        }

        private async void ButtonSearch_Click(object sender, RoutedEventArgs e)
        {
            if (Manager.ItemList.Count == 0 || string.IsNullOrWhiteSpace(Filter))
                return;

            try
            {
                IsBusy = true;
                await Manager.LoadIpAll();
                IsBusy = false;
            }
            catch (ProgressWindowClosedException)
            {

            }

            if (dg1.SelectedIndex == -1 || !searchInGrid(dg1.SelectedIndex))
                searchInGrid(0);
        }

        private bool searchInGrid(int start)
        {
            for (int i = start; i < Manager.ItemList.Count; i++)
            {
                var item = Manager.ItemList[i];
                if (dg1.SelectedItem != item && Manager.SearchInItem(item, Filter))
                {
                    dg1.SelectedItem = item;
                    dg1.ScrollIntoView(item);
                    return true;
                }
            }
            return false;
        }

        private void FolderComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Refresh known folders list to include any newly typed values
            Manager.InitializeKnownFolders();

            // Store the original folder value
            if (sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                _originalFolderValue = item.Folder;
            }
        }

        private void FolderComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            // If user presses Enter on empty text, clear the folder value
            if (e.Key == Key.Enter && sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                if (string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    // Clear the folder value
                    item.Folder = string.Empty;

                    // Clear the original value so LostFocus doesn't restore it
                    _originalFolderValue = null;

                    // Move focus away to exit edit mode and commit the change
                    dg1.Focus();

                    e.Handled = true;
                }
            }
        }

        private void FolderComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // If the user didn't select anything and the value is now empty, restore the original value
            if (sender is ComboBox comboBox && comboBox.DataContext is GdItem item)
            {
                if (string.IsNullOrWhiteSpace(comboBox.Text) && !string.IsNullOrWhiteSpace(_originalFolderValue))
                {
                    item.Folder = _originalFolderValue;
                }
                _originalFolderValue = null;
            }
        }

    }
}
