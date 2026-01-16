using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GongSolutions.Wpf.DragDrop;
using GongSolutions.Wpf.DragDrop.Utilities;

namespace GDMENUCardManager
{
    internal static class DragDropHandler
    {
        public static void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo == null)
            {
                if (dropInfo.Data is DataObject data && data.ContainsFileDropList())
                    dropInfo.Effects = DragDropEffects.Copy;
            }
            else if (DefaultDropHandler.CanAcceptData(dropInfo))
            {
                // Check if the dragged item is a menu item
                var draggedItems = DefaultDropHandler.ExtractData(dropInfo.Data).OfType<GdItem>().ToList();
                bool hasMenuItem = draggedItems.Any(item => item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu");

                if (hasMenuItem)
                {
                    // Don't allow dragging menu items
                    dropInfo.Effects = DragDropEffects.None;
                }
                else if (dropInfo.UnfilteredInsertIndex == 0)
                {
                    // Don't allow dropping items at position 0 (would push menu down)
                    dropInfo.Effects = DragDropEffects.None;
                }
                else
                {
                    dropInfo.Effects = DragDropEffects.Move;
                }
            }

            if (dropInfo.Effects != DragDropEffects.None)
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        }

        public static async Task Drop(IDropInfo dropInfo)
        {
            var invalid = new List<string>();

            var insertIndex = dropInfo.UnfilteredInsertIndex;
            var destinationList = dropInfo.TargetCollection.TryGetList();

            if (dropInfo.DragInfo == null)
            {
                if (!(dropInfo.Data is DataObject data) || !data.ContainsFileDropList())
                    return;

                foreach (var o in data.GetFileDropList())
                {
                    try
                    {
                        var toInsert = await ImageHelper.CreateGdItemAsync(o);
                        destinationList.Insert(insertIndex++, toInsert);
                    }
                    catch
                    {
                        invalid.Add(o);
                    }
                }
            }
            else
            {
                var data = DefaultDropHandler.ExtractData(dropInfo.Data).OfType<object>().ToList();

                var sourceList = dropInfo.DragInfo.SourceCollection.TryGetList();
                if (sourceList != null)
                {
                    foreach (var o in data)
                    {
                        var index = sourceList.IndexOf(o);
                        if (index != -1)
                        {
                            sourceList.RemoveAt(index);
                            if (destinationList != null && Equals(sourceList, destinationList) && index < insertIndex)
                                --insertIndex;
                        }
                    }
                }

                if (destinationList != null)
                    foreach (var o in data)
                        destinationList.Insert(insertIndex++, o);
            }

            if (invalid.Any())
                throw new InvalidDropException(string.Join(Environment.NewLine, invalid));
        }
    }

    internal class InvalidDropException : Exception
    {
        public InvalidDropException(string message) : base(message)
        {
        }
    }
}
