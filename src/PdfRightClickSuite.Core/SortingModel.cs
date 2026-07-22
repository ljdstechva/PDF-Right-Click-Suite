namespace PdfRightClickSuite.Core;

public sealed class SortingModel
{
    private readonly string[] _originalItems;
    private readonly List<string> _items;

    public SortingModel(IEnumerable<string> items)
    {
        _originalItems = items.ToArray();
        _items = _originalItems.ToList();
    }

    public IReadOnlyList<string> Items => _items;

    public int SelectedIndex { get; private set; }

    public void SelectPrevious()
    {
        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
    }

    public void SelectNext()
    {
        if (SelectedIndex < _items.Count - 1)
        {
            SelectedIndex++;
        }
    }

    public void MoveSelectedLeft()
    {
        if (SelectedIndex <= 0)
        {
            return;
        }

        var item = _items[SelectedIndex];
        _items[SelectedIndex] = _items[SelectedIndex - 1];
        _items[SelectedIndex - 1] = item;
        SelectedIndex--;
    }

    public void MoveSelectedRight()
    {
        if (SelectedIndex >= _items.Count - 1)
        {
            return;
        }

        var item = _items[SelectedIndex];
        _items[SelectedIndex] = _items[SelectedIndex + 1];
        _items[SelectedIndex + 1] = item;
        SelectedIndex++;
    }

    public void Reset()
    {
        _items.Clear();
        _items.AddRange(_originalItems);
        SelectedIndex = 0;
    }
}
