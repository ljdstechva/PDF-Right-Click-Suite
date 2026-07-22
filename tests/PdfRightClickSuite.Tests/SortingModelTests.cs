using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class SortingModelTests
{
    [Fact]
    public void MoveSelectedLeft_does_nothing_for_first_item()
    {
        var model = new SortingModel(["a.pdf", "b.pdf", "c.pdf"]);

        model.MoveSelectedLeft();

        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, model.Items);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void MoveSelectedRight_does_nothing_for_last_item()
    {
        var model = new SortingModel(["a.pdf", "b.pdf", "c.pdf"]);
        model.SelectNext();
        model.SelectNext();

        model.MoveSelectedRight();

        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, model.Items);
        Assert.Equal(2, model.SelectedIndex);
    }

    [Fact]
    public void MoveSelectedLeft_and_right_reorder_middle_item()
    {
        var model = new SortingModel(["a.pdf", "b.pdf", "c.pdf"]);
        model.SelectNext();

        model.MoveSelectedLeft();
        Assert.Equal(new[] { "b.pdf", "a.pdf", "c.pdf" }, model.Items);
        Assert.Equal(0, model.SelectedIndex);

        model.MoveSelectedRight();
        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, model.Items);
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void SelectPrevious_and_next_clamp_to_bounds()
    {
        var model = new SortingModel(["a.pdf", "b.pdf"]);

        model.SelectPrevious();
        Assert.Equal(0, model.SelectedIndex);

        model.SelectNext();
        model.SelectNext();
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void Reset_restores_original_order_and_first_selection()
    {
        var model = new SortingModel(["a.pdf", "b.pdf", "c.pdf"]);
        model.SelectNext();
        model.MoveSelectedRight();

        model.Reset();

        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, model.Items);
        Assert.Equal(0, model.SelectedIndex);
    }
}
