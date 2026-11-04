using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace InventoryManagement.App.Controls;

// Building blocks shared by every page, so the list/edit layouts stay
// consistent. Templates live in Styles/Components.xaml.

/// <summary>Page title + subtitle on the left, the page's action buttons (Content) on the right.</summary>
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader));

    static PageHeader() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
}

/// <summary>Small KPI card: coloured icon tile, title, big value with an optional badge, caption.</summary>
public sealed class StatCard : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(object), typeof(StatCard));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(SymbolRegular), typeof(StatCard));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(StatCard));
    public static readonly DependencyProperty AccentSoftProperty = DependencyProperty.Register(nameof(AccentSoft), typeof(Brush), typeof(StatCard));

    static StatCard() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatCard), new FrameworkPropertyMetadata(typeof(StatCard)));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Optional small pill next to the value, e.g. "50%".</summary>
    public string? Badge { get => (string?)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

    public string? Caption { get => (string?)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    public SymbolRegular Icon { get => (SymbolRegular)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush? AccentSoft { get => (Brush?)GetValue(AccentSoftProperty); set => SetValue(AccentSoftProperty, value); }
}

/// <summary>
/// Search box with an icon and placeholder. Runs <see cref="SubmitCommand"/>
/// on Enter and also shortly after the user stops typing, so lists filter
/// as you type without a separate Search button.
/// </summary>
[TemplatePart(Name = "PART_TextBox", Type = typeof(TextBox))]
public sealed class SearchField : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchField));

    public static readonly DependencyProperty SubmitCommandProperty =
        DependencyProperty.Register(nameof(SubmitCommand), typeof(ICommand), typeof(SearchField));

    /// <summary>
    /// False for boxes where submitting has a side effect (e.g. "add this
    /// product to the document") - those only submit on Enter, never on a
    /// typing pause.
    /// </summary>
    public static readonly DependencyProperty SubmitOnTypingProperty =
        DependencyProperty.Register(nameof(SubmitOnTyping), typeof(bool), typeof(SearchField), new PropertyMetadata(true));

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private TextBox? _textBox;

    static SearchField() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchField), new FrameworkPropertyMetadata(typeof(SearchField)));

    public SearchField()
    {
        _debounce.Tick += (_, _) => Submit();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public ICommand? SubmitCommand { get => (ICommand?)GetValue(SubmitCommandProperty); set => SetValue(SubmitCommandProperty, value); }

    public bool SubmitOnTyping { get => (bool)GetValue(SubmitOnTypingProperty); set => SetValue(SubmitOnTypingProperty, value); }

    /// <summary>Moves keyboard focus into the text box (e.g. after adding an item, ready for the next scan).</summary>
    public void FocusInput() => _textBox?.Focus();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox is not null)
        {
            _textBox.KeyDown -= OnTextBoxKeyDown;
        }

        _textBox = GetTemplateChild("PART_TextBox") as TextBox;
        if (_textBox is not null)
        {
            _textBox.KeyDown += OnTextBoxKeyDown;
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (SearchField)d;

        // Only the user typing should trigger a search - not the ViewModel
        // setting the text (e.g. "Clear Filters"), which reloads by itself.
        if (!field.SubmitOnTyping || field._textBox is null || !field._textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        field._debounce.Stop();
        field._debounce.Start();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
        {
            Text = string.Empty;
            if (SubmitOnTyping)
            {
                Submit(); // re-run the now-unfiltered search
            }

            e.Handled = true;
        }
    }

    private void Submit()
    {
        _debounce.Stop();
        if (SubmitCommand?.CanExecute(null) == true)
        {
            SubmitCommand.Execute(null);
        }
    }
}

/// <summary>
/// "Showing 1 to 10 of 42 products", previous/next buttons around the
/// current page number, and a per-page size picker. Binds straight to the
/// paging properties every list ViewModel already has.
/// </summary>
public sealed class Pager : Control
{
    public static readonly DependencyProperty PageNumberProperty = DependencyProperty.Register(
        nameof(PageNumber), typeof(int), typeof(Pager), new PropertyMetadata(1, OnPagingChanged));

    public static readonly DependencyProperty TotalPagesProperty = DependencyProperty.Register(
        nameof(TotalPages), typeof(int), typeof(Pager), new PropertyMetadata(1, OnPagingChanged));

    public static readonly DependencyProperty TotalCountProperty = DependencyProperty.Register(
        nameof(TotalCount), typeof(int), typeof(Pager), new PropertyMetadata(0, OnPagingChanged));

    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize), typeof(int), typeof(Pager),
        new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPagingChanged));

    public static readonly DependencyProperty ItemNameProperty = DependencyProperty.Register(
        nameof(ItemName), typeof(string), typeof(Pager), new PropertyMetadata("items", OnPagingChanged));

    public static readonly DependencyProperty PreviousCommandProperty =
        DependencyProperty.Register(nameof(PreviousCommand), typeof(ICommand), typeof(Pager));

    public static readonly DependencyProperty NextCommandProperty =
        DependencyProperty.Register(nameof(NextCommand), typeof(ICommand), typeof(Pager));

    private static readonly DependencyPropertyKey SummaryPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Summary), typeof(string), typeof(Pager), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SummaryProperty = SummaryPropertyKey.DependencyProperty;

    static Pager() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Pager), new FrameworkPropertyMetadata(typeof(Pager)));

    public static IReadOnlyList<int> PageSizeOptions { get; } = new[] { 10, 25, 50, 100 };

    public int PageNumber { get => (int)GetValue(PageNumberProperty); set => SetValue(PageNumberProperty, value); }

    public int TotalPages { get => (int)GetValue(TotalPagesProperty); set => SetValue(TotalPagesProperty, value); }

    public int TotalCount { get => (int)GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }

    public int PageSize { get => (int)GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }

    public string ItemName { get => (string)GetValue(ItemNameProperty); set => SetValue(ItemNameProperty, value); }

    public ICommand? PreviousCommand { get => (ICommand?)GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }

    public ICommand? NextCommand { get => (ICommand?)GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }

    public string Summary => (string)GetValue(SummaryProperty);

    private static void OnPagingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pager = (Pager)d;
        if (pager.TotalCount <= 0)
        {
            pager.SetValue(SummaryPropertyKey, $"No {pager.ItemName} to show");
            return;
        }

        var first = (pager.PageNumber - 1) * pager.PageSize + 1;
        var last = Math.Min(pager.PageNumber * pager.PageSize, pager.TotalCount);
        pager.SetValue(SummaryPropertyKey, $"Showing {first} to {last} of {pager.TotalCount} {pager.ItemName}");
    }
}

/// <summary>An edit-form section: tinted header strip (icon, title, subtitle) above its fields.</summary>
public sealed class FormSection : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(FormSection));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(FormSection));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(SymbolRegular), typeof(FormSection));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(FormSection));
    public static readonly DependencyProperty AccentSoftProperty = DependencyProperty.Register(nameof(AccentSoft), typeof(Brush), typeof(FormSection));

    static FormSection() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormSection), new FrameworkPropertyMetadata(typeof(FormSection)));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public SymbolRegular Icon { get => (SymbolRegular)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush? AccentSoft { get => (Brush?)GetValue(AccentSoftProperty); set => SetValue(AccentSoftProperty, value); }
}

/// <summary>Label above an input: "Name *" for required fields, "Barcode (Optional)" for optional ones.</summary>
public sealed class FormField : ContentControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(FormField));
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(nameof(IsRequired), typeof(bool), typeof(FormField));
    public static readonly DependencyProperty IsOptionalProperty = DependencyProperty.Register(nameof(IsOptional), typeof(bool), typeof(FormField));

    static FormField() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormField), new FrameworkPropertyMetadata(typeof(FormField)));

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    public bool IsOptional { get => (bool)GetValue(IsOptionalProperty); set => SetValue(IsOptionalProperty, value); }
}

/// <summary>
/// The "⋯" button at the end of a table row: opens its ContextMenu on a
/// normal left click. Menu items reach the page's commands through
/// PlacementTarget.Tag (set Tag to the page ViewModel) because a
/// ContextMenu is not part of the page's visual tree.
/// </summary>
public sealed class RowActionsButton : Button
{
    static RowActionsButton() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RowActionsButton), new FrameworkPropertyMetadata(typeof(RowActionsButton)));

    protected override void OnClick()
    {
        base.OnClick();
        if (ContextMenu is { } menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
