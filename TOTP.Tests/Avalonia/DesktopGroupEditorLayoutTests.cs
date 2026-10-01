using System.Xml.Linq;

namespace TOTP.Tests.Avalonia;

public sealed class DesktopGroupEditorLayoutTests
{
    [Fact]
    public void AccountCount_UsesPersistentCompactCenteredFooter()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopMainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var accountPageContent = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountPageContent");
        Assert.Equal("4", accountPageContent.Attribute("Spacing")?.Value);

        var footer = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountCountFooter");
        Assert.Equal("1", footer.Attribute("Grid.Row")?.Value);
        Assert.Equal("22", footer.Attribute("Height")?.Value);
        Assert.Equal("{Binding HasAnyAccounts}", footer.Attribute("IsVisible")?.Value);

        var summary = footer.Elements(avalonia + "TextBlock").Single();
        Assert.Null(summary.Attribute("Margin"));
        Assert.Equal("Center", summary.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", summary.Attribute("TextAlignment")?.Value);
        Assert.Equal("10", summary.Attribute("FontSize")?.Value);
        Assert.Equal("NoWrap", summary.Attribute("TextWrapping")?.Value);
    }

    [Fact]
    public void GroupCards_ConstrainNamesToSingleLineWithSymmetricContentPadding()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopMainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var groupName = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding Name}"
                && element.Ancestors(avalonia + "ItemsControl").Any(item =>
                    item.Attribute("ItemsSource")?.Value == "{Binding Groups}"));
        Assert.Equal("CharacterEllipsis", groupName.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", groupName.Attribute("TextWrapping")?.Value);

        var contentGrid = groupName
            .Ancestors(avalonia + "Grid")
            .First(element => element.Attribute("ColumnDefinitions")?.Value == "20,*");
        Assert.Equal("7", contentGrid.Attribute("ColumnSpacing")?.Value);
    }

    [Fact]
    public void AccountSelectionRows_CenterControlsAndSeparateCheckboxFromBrand()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopMainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var row = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GroupAccountSelectionRow");
        Assert.Equal("44", row.Attribute("MinHeight")?.Value);

        var checkbox = row
            .Elements(avalonia + "CheckBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GroupAccountSelectionCheckBox");
        Assert.Equal("Center", checkbox.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("0,0,8,0", checkbox.Attribute("Margin")?.Value);

        var brand = row
            .Elements()
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GroupAccountSelectionBrand");
        Assert.Equal("Center", brand.Attribute("VerticalAlignment")?.Value);

        var text = row
            .Elements(avalonia + "StackPanel")
            .Single();
        Assert.Equal("Center", text.Attribute("VerticalAlignment")?.Value);
    }
}
