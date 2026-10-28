using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountInteractionTests
{
    [Fact]
    public void AndroidActivity_ResizesTheAppViewportAboveTheSoftwareKeyboard()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "MainActivity.cs"));

        Assert.Contains("WindowSoftInputMode = SoftInput.AdjustResize", source);
    }

    [Fact]
    public void AndroidScreenCaptureProtection_FollowsTheRuntimePolicyAcrossTheApp()
    {
        var activitySource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "MainActivity.cs"));
        var scannerSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "LiveQrScannerActivity.cs"));
        var applicationSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "OtpHarborApplication.cs"));

        Assert.Contains(
            "_screenCapturePolicy?.IsScreenCaptureProtectionRequired != false",
            activitySource);
        Assert.Contains("ApplyScreenCapturePolicy();", activitySource);
        Assert.Contains("RunOnUiThread", activitySource);
        Assert.Contains("window.SetFlags(flags, WindowManagerFlags.Secure);", activitySource);
        Assert.Contains("host.IsScreenCaptureProtectionRequired", scannerSource);
        Assert.Contains("Window?.SetFlags(flags, WindowManagerFlags.Secure);", scannerSource);
        Assert.Contains("internal bool IsScreenCaptureProtectionRequired", applicationSource);
    }

    [Fact]
    public void AndroidUnhandledExceptions_AreWrittenToTheSystemLogThroughTheRedactor()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "OtpHarborApplication.cs"));

        Assert.Contains("AndroidEnvironment.UnhandledExceptionRaiser +=", source);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException +=", source);
        Assert.Contains("SensitiveTextRedactor.Sanitize", source);
        Assert.Contains("\"OtpHarborCrash\"", source);
        Assert.DoesNotContain("Log.Fatal(\n            exception,", source);
    }

    [Fact]
    public void AndroidAutomaticBackupRetention_UsesTheSafDocumentDeleteContract()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "AndroidAutomaticBackupDestinationAdapter.cs"));

        Assert.Contains("DocumentsContract.DeleteDocument(resolver, documentUri)", source);
        Assert.DoesNotContain("resolver.Delete(documentUri", source);
    }

    [Fact]
    public void AndroidAccountSwipe_OpensDeletePromptWithoutQueueingBehindDispatcherWork()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountRowView.cs"));

        Assert.Contains("if (Dispatcher.UIThread.CheckAccess())", source);
        Assert.Contains("_ = action();", source);
        Assert.Contains("BeginDeleteForAccountAsync(_row?.Account)", source);
    }

    [Fact]
    public void AndroidNativeAccountList_DisposalToleratesAnAlreadyDestroyedNativePeer()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountRecyclerView.cs"));

        Assert.Contains("if (_disposed) return;", source);
        Assert.Contains("_disposed = true;", source);
        Assert.DoesNotContain("RemoveOnScrollListener(_scrollListener);", source);
        Assert.DoesNotContain("SetAdapter(null);", source);
    }

    [Fact]
    public void AndroidAccountList_ShowsImmediatelyAndPrewarmsSixHundredIntoAnApplicationCache()
    {
        var recyclerSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountRecyclerView.cs"));
        var rowSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountRowView.cs"));
        var iconSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeBrandIcon.cs"));
        var cacheSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeBrandIconCache.cs"));
        var applicationSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "OtpHarborApplication.cs"));

        Assert.Contains("_adapter.SetListScrolling(scrolling);", recyclerSource);
        Assert.Contains("holder.Row.SetListScrolling(scrolling);", recyclerSource);
        Assert.Contains("Bind(_items[position], _isListScrolling)", recyclerSource);
        Assert.Contains("if (_animating || _row is null || _isListScrolling) return;", rowSource);
        Assert.Contains("if (_brandIcon is null) PrepareBrandIcon();", rowSource);
        Assert.Contains("FullIconPrewarmAccountLimit = 640", recyclerSource);
        Assert.Contains("FullIconPrewarmBrandLimit = 40", recyclerSource);
        Assert.Contains("InitialIconPrewarmAccountCount = 640", recyclerSource);
        Assert.Contains("IconPrewarmBatchSize = 100", recyclerSource);
        Assert.Contains("RuntimeIconPrewarmConcurrency = 16", recyclerSource);
        Assert.Contains("RefreshIconPrewarmProfile();", recyclerSource);
        var adapterAttach = recyclerSource.IndexOf("SetAdapter(_adapter);", StringComparison.Ordinal);
        var initialSchedule = recyclerSource.IndexOf("_ = StartInitialIconPrewarm();", StringComparison.Ordinal);
        Assert.True(adapterAttach >= 0);
        Assert.True(initialSchedule > adapterAttach);
        Assert.Contains("if (requestKey == _iconPrewarmRequestKey) return;", recyclerSource);
        Assert.Contains("AddBatch(currentBatch);", recyclerSource);
        Assert.Contains("AddBatch(lookaheadBatch);", recyclerSource);
        Assert.Contains("_hasAccountListScrolled = true;", recyclerSource);
        Assert.Contains("CancelIconPrewarm();", recyclerSource);
        Assert.Contains("Post(ScheduleIconPrewarm);", recyclerSource);
        Assert.Contains("Task.Run(", recyclerSource);
        Assert.Contains("TryGetMaterializedBrand(metadata, out var cached)", recyclerSource);
        Assert.Contains("finally", recyclerSource);
        Assert.Contains("prepared.TrySetResult();", recyclerSource);
        Assert.Contains("owner.HandleIdleScroll();", recyclerSource);
        Assert.Contains("owner.ScheduleIconPrewarm();", recyclerSource);
        Assert.Contains("TryGetMaterializedBrand(account.Brand", rowSource);
        Assert.Contains("allowTemplateCreation: !_isListScrolling", rowSource);
        Assert.Contains("public NativeBrandIcon Clone()", iconSource);
        Assert.Contains("return existing.Template.Clone();", cacheSource);
        Assert.Contains("IconPayloadMatches(existing, brand)", cacheSource);
        Assert.Contains("existing.IconLayers.SequenceEqual(brand.IconLayers)", cacheSource);
        Assert.DoesNotContain("ReferenceEquals(existing.Brand, brand)", cacheSource);
        Assert.Contains("PrewarmRuntimeAsync", recyclerSource);
        Assert.Contains("Parallel.ForEachAsync", recyclerSource);
        Assert.Contains("_initialIconPrewarmLifetime", recyclerSource);
        Assert.Contains("Math.Min(items.Length, InitialIconPrewarmAccountCount)", recyclerSource);
        Assert.Contains("CancelAllIconPrewarm();", recyclerSource);
        Assert.Contains("SetPinnedRuntimeIcons(pinnedBrands)", recyclerSource);
        Assert.DoesNotContain("AttachAdapterAfterPrewarmAsync", recyclerSource);
        Assert.Contains("if (metadata.HasRuntimeIcon)", recyclerSource);
        Assert.Contains("if (account.Brand.HasRuntimeIcon)", rowSource);
        Assert.Contains("var inset = appearance is null ? Dp(9) : 0;", rowSource);
        Assert.Contains(
            "_drawBrandBackground = appearance?.BackgroundMode != BrandIconBackgroundMode.Embedded;",
            rowSource);
        Assert.DoesNotContain("File.ReadAllBytes", cacheSource);
        Assert.DoesNotContain("SHA256.HashData", cacheSource);
        Assert.Contains("load = Task.Run(", cacheSource);
        Assert.Contains("MaximumRuntimeEntries = 640", cacheSource);
        Assert.Contains("MaximumRuntimeBytes = 48L * 1024 * 1024", cacheSource);
        Assert.Contains("_pinnedRuntimeKeys", cacheSource);
        Assert.Contains("!_pinnedRuntimeKeys.Contains(pair.Key)", cacheSource);
        Assert.Contains("RuntimeBrandIconAssetReader.ReadValidatedAsync", cacheSource);
        Assert.Contains("BitmapFactory.DecodeByteArray", cacheSource);
        Assert.DoesNotContain("BitmapFactory.DecodeFile", cacheSource);
        Assert.Contains("RuntimeIconFor(brand) is", cacheSource);
        Assert.Contains("PrepareBrandIcon(bool requireRuntimeIconOnFirstDraw = true)", rowSource);
        Assert.Contains("requireRuntimeIconOnFirstDraw);", rowSource);
        Assert.Contains("if (requireRuntimeIconOnFirstDraw)", cacheSource);
        Assert.Contains(".ConfigureAwait(false)", cacheSource);
        Assert.Contains(".GetAwaiter()", cacheSource);
        Assert.Contains(".GetResult();", cacheSource);
        Assert.Contains("_brandIconCache ??= new NativeBrandIconCache();", applicationSource);
        Assert.Contains("_brandIconCache?.Dispose();", applicationSource);
        Assert.DoesNotContain("_brandIcons.Dispose();", recyclerSource);
        Assert.Contains("revision != Volatile.Read(ref _submitRevision)", recyclerSource);
        Assert.Contains("_adapter.ReplaceAll(CaptureRows());", recyclerSource);
        Assert.Contains(
            "args.PropertyName == nameof(MobileShellViewModel.AccountBrandCatalogRevision)",
            recyclerSource);
        var catalogRefresh = recyclerSource.IndexOf(
            "args.PropertyName == nameof(MobileShellViewModel.AccountBrandCatalogRevision)",
            StringComparison.Ordinal);
        var catalogSubmit = recyclerSource.IndexOf("SubmitRows();", catalogRefresh, StringComparison.Ordinal);
        Assert.True(catalogRefresh >= 0);
        Assert.True(catalogSubmit > catalogRefresh);
        Assert.Contains("HasStableIds = true;", recyclerSource);
        Assert.DoesNotContain("DiffUtil.CalculateDiff", recyclerSource);
    }

    [Fact]
    public void AndroidQrScanner_OffersALocalImageWithoutDelayingTheLiveCamera()
    {
        var activitySource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "LiveQrScannerActivity.cs"));
        var scannerSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "AndroidQrScanner.cs"));
        var mainViewSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));

        Assert.Contains("StartCamera();", activitySource);
        Assert.Contains("Intent.ActionOpenDocument", activitySource);
        Assert.Contains("intent.SetType(\"image/*\")", activitySource);
        Assert.Contains("MaximumEncodedImageBytes", activitySource);
        Assert.Contains("MaximumDecodedImageDimension", activitySource);
        Assert.Contains("Array.Clear(encoded);", activitySource);
        Assert.Contains("Array.Clear(luminance);", activitySource);
        Assert.Contains("RunOnUiThread(() => Complete(payload, null));", activitySource);
        Assert.Contains("RunOnUiThread(ShowImageScanFailure);", activitySource);
        Assert.Contains("MobileStringKeys.UploadQrImage", scannerSource);
        Assert.DoesNotContain("RequestPermissions([Manifest.Permission.Read", activitySource);
        Assert.Contains("IsQrImportResolutionVisible", mainViewSource);
        Assert.Contains("SkipAllQrConflictsCommand", mainViewSource);
        Assert.Contains("ReplaceAllQrConflictsCommand", mainViewSource);
        Assert.Contains("KeepBothAllQrConflictsCommand", mainViewSource);
        Assert.Contains("CancelQrImportResolutionCommand", mainViewSource);
        Assert.Contains("Content=\"{Binding ImportCancelText}\"", mainViewSource);
        Assert.Contains("IsQrImportResultVisible", mainViewSource);
        Assert.Contains("DismissQrImportResultCommand", mainViewSource);
    }

    [Fact]
    public void AndroidGroupStrip_RedrawsRegularGroupsWithAnOpaqueStableBackground()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountGroupRecyclerView.cs"));

        Assert.Contains(
            ": BlendOver(_groupColor, palette.WindowBackground, 52);",
            source);
        Assert.Contains("Color WindowBackground", source);
        Assert.DoesNotContain(": WithAlpha(_groupColor, 52);", source);
    }

    [Fact]
    public void AndroidGroupStrip_RebindsTheCompleteSmallDataSetAfterGroupChanges()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeAccountGroupRecyclerView.cs"));

        Assert.Contains("internal void Submit(NativeAccountGroupRow[] items)", source);
        Assert.Equal(2, source.Split("NotifyDataSetChanged();").Length - 1);
        Assert.DoesNotContain("DiffUtil.CalculateDiff", source);
        Assert.DoesNotContain("GroupDiffCallback", source);
    }

    [Fact]
    public void MobileIconPicker_AppliesAvaloniaInputPaneOcclusionToItsOverlay()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml.cs"));

        Assert.Contains("inputPane.StateChanged += InputPaneStateChanged", source);
        Assert.Contains("state == InputPaneState.Open", source);
        Assert.Contains("Math.Max(0, occludedRect.Height)", source);
        Assert.Contains("AccountBrandIconPickerModalOverlay.Margin", source);
    }

    [Fact]
    public void AndroidDeleteConfirmation_KeepsTheNativeAccountListBehindANativeDialog()
    {
        var viewSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml.cs"));
        var dialogSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "NativeDeleteConfirmationDialog.cs"));
        var applicationSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "OtpHarborApplication.cs"));

        Assert.Contains("AccountList.IsVisible = false", viewSource);
        Assert.Contains("viewModel.IsSharedDeleteConfirmationVisible", viewSource);
        Assert.Contains("viewModel.UseNativeDeleteConfirmation()", applicationSource);
        Assert.Contains("new AlertDialog.Builder", dialogSource);
        Assert.Contains("builder.SetView(CreateContent())", dialogSource);
        Assert.Contains("CreateRoundedBackground", dialogSource);
        Assert.Contains("window.SetLayout(width", dialogSource);
        Assert.Contains("content.SetPadding(Dp(22)", dialogSource);
        Assert.Contains("Color.ParseColor(\"#B42318\")", dialogSource);
        Assert.Contains("ConfirmDeleteAsync()", dialogSource);
        Assert.Contains("CancelDeleteAsync()", dialogSource);

        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var deleteOverlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute("IsVisible")?.Value ==
                "{Binding IsSharedDeleteConfirmationVisible}");
        Assert.Equal("101", deleteOverlay.Attribute("Panel.ZIndex")?.Value);
    }

    [Fact]
    public void OnboardingSlides_UseFullScreenArtworkWithLocalizedOverlayControls()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var slides = new[]
        {
            (Visibility: "{Binding IsOnboardingAddAccountsVisible}", Title: "{Binding OnboardingAddAccountsTitleText}", Artwork: "{DynamicResource MobileOnboardingAddAccountsArtwork}"),
            (Visibility: "{Binding IsOnboardingMoveAccountsVisible}", Title: "{Binding OnboardingMoveAccountsTitleText}", Artwork: "{DynamicResource MobileOnboardingMoveAccountsArtwork}"),
            (Visibility: "{Binding IsOnboardingOrganizeAccountsVisible}", Title: "{Binding OnboardingOrganizeAccountsTitleText}", Artwork: "{DynamicResource MobileOnboardingOrganizeAccountsArtwork}"),
            (Visibility: "{Binding IsOnboardingIconPackVisible}", Title: "{Binding OnboardingIconPackTitleText}", Artwork: "{DynamicResource MobileOnboardingIconPackArtwork}"),
            (Visibility: "{Binding IsOnboardingQuickUnlockVisible}", Title: "{Binding OnboardingQuickUnlockTitleText}", Artwork: "{DynamicResource MobileOnboardingQuickUnlockArtwork}")
        };

        var overlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "MobileOnboardingOverlay");
        Assert.Equal("3", overlay.Attribute("Grid.RowSpan")?.Value);

        foreach (var expected in slides)
        {
            var image = overlay
                .Descendants(avalonia + "Image")
                .Single(element => element.Attribute("IsVisible")?.Value == expected.Visibility);
            Assert.Equal(expected.Artwork, image.Attribute("Source")?.Value);
            Assert.Equal("UniformToFill", image.Attribute("Stretch")?.Value);

            var textPanel = overlay
                .Descendants(avalonia + "StackPanel")
                .Single(element => element.Attribute("IsVisible")?.Value == expected.Visibility);
            var intent = Assert.Single(
                textPanel.Descendants(avalonia + "TextBlock"),
                element => element.Attribute("Text")?.Value == expected.Title);
            Assert.Equal(
                "{DynamicResource BrushMobileOnboardingForeground}",
                intent.Attribute("Foreground")?.Value);
        }

        var skipButton = overlay
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SkipOnboardingButton");
        Assert.Equal("{Binding FinishOnboardingCommand}", skipButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsFirstOnboardingSlide}", skipButton.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding OnboardingSkipText}", skipButton.Attribute("Content")?.Value);
        Assert.Equal("mobile-onboarding-action", skipButton.Attribute("Classes")?.Value);
        Assert.Equal("0", skipButton.Attribute("Grid.Column")?.Value);

        var actionStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Button.mobile-onboarding-action");
        var actionSetters = actionStyle
            .Elements(avalonia + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? string.Empty,
                element => element.Attribute("Value")?.Value ?? string.Empty);
        Assert.Equal("Transparent", actionSetters["Background"]);
        Assert.Equal("1", actionSetters["BorderThickness"]);
        Assert.Equal(
            "{DynamicResource BrushMobileOnboardingAccentText}",
            actionSetters["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource BrushMobileOnboardingAccentText}",
            actionSetters["Foreground"]);
        Assert.Equal("Center", actionSetters["VerticalContentAlignment"]);

        var actionTemplate = actionStyle
            .Elements(avalonia + "Setter")
            .Single(element => element.Attribute("Property")?.Value == "Template")
            .Descendants(avalonia + "ControlTemplate")
            .Single();
        var actionContent = actionTemplate
            .Descendants(avalonia + "ContentPresenter")
            .Single();
        Assert.Equal("OnboardingButtonContent", actionContent.Attribute("Name")?.Value);
        Assert.Equal(
            "{TemplateBinding VerticalContentAlignment}",
            actionContent.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal(
            "{TemplateBinding Foreground}",
            actionContent.Attribute("TextElement.Foreground")?.Value);
        Assert.DoesNotContain(
            actionTemplate.Descendants(),
            element => element.Attribute("Name")?.Value is "PART_Border" or "PART_ContentPresenter");

        var pressedStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Button.mobile-onboarding-action:pressed");
        var pressedSetters = pressedStyle
            .Elements(avalonia + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? string.Empty,
                element => element.Attribute("Value")?.Value ?? string.Empty);
        Assert.Equal("Transparent", pressedSetters["Background"]);
        Assert.Equal(actionSetters["BorderBrush"], pressedSetters["BorderBrush"]);
        Assert.Equal(actionSetters["Foreground"], pressedSetters["Foreground"]);

        var navigation = Assert.IsType<XElement>(skipButton.Parent);
        Assert.Equal("Auto,*,Auto", navigation.Attribute("ColumnDefinitions")?.Value);
        Assert.All(
            navigation.Elements(avalonia + "Button"),
            button => Assert.Equal("mobile-onboarding-action", button.Attribute("Classes")?.Value));
        var navigationButtons = navigation.Elements(avalonia + "Button").ToArray();
        foreach (var expected in new[]
                 {
                     (Command: "{Binding PreviousOnboardingCommand}", Visibility: "{Binding !IsFirstOnboardingSlide}"),
                     (Command: "{Binding NextOnboardingCommand}", Visibility: "{Binding !IsLastOnboardingSlide}"),
                     (Command: "{Binding FinishOnboardingCommand}", Visibility: "{Binding IsLastOnboardingSlide}")
                 })
        {
            var button = navigationButtons.Single(
                element => element.Attribute("Command")?.Value == expected.Command
                    && element.Attribute("IsVisible")?.Value == expected.Visibility);
            Assert.Equal("mobile-onboarding-action", button.Attribute("Classes")?.Value);
        }

        var contentPanel = Assert.IsType<XElement>(
            overlay.Descendants(avalonia + "Border").Single());
        Assert.Equal(
            "{DynamicResource BrushMobileOnboardingPanel}",
            contentPanel.Attribute("Background")?.Value);
    }

    [Fact]
    public void ImportedIconRemoval_UsesDeleteSymbol()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var removeButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding ResetBrandIconsCommand}");
        var label = removeButton
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIconLabel");

        Assert.Equal("Delete", label.Attribute("Kind")?.Value);
    }

    [Fact]
    public void AppHeader_UsesOtpHarborLogoInsteadOfLockSymbol()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var header = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "MobileAppHeader");
        var logo = header
            .Descendants(avalonia + "Image")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "MobileAppLogo");
        var headerContent = Assert.Single(header.Elements(avalonia + "Grid"));

        Assert.Equal(
            "avares://TOTP.UI.Avalonia.Shared/Assets/Biometric/otp-harbor-mark.png",
            logo.Attribute("Source")?.Value);
        Assert.Equal("48", headerContent.Attribute("MinHeight")?.Value);
        Assert.DoesNotContain(
            header.Elements(),
            element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Grid.Column")?.Value == "0");
    }

    [Fact]
    public void AccountEditor_AdvancedOptionsOwnFavoriteAndBrandIconControls()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace controls = "using:TOTP.Avalonia.Shared.Controls";

        var advanced = document
            .Descendants(avalonia + "Expander")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountAdvancedOptions");

        Assert.Contains(
            advanced.Descendants(avalonia + "CheckBox"),
            control => control.Attribute("IsChecked")?.Value
                == "{Binding EditorIsFavorite, Mode=TwoWay}");
        var brandIconPicker = advanced
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconPickerButton");
        Assert.Equal("account-brand-icon-picker", brandIconPicker.Attribute("Classes")?.Value);
        Assert.Equal(
            "{Binding OpenAccountBrandIconPickerCommand}",
            brandIconPicker.Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding HasSearchableBrandIcons}",
            brandIconPicker.Attribute("IsVisible")?.Value);
        Assert.Equal("Stretch", brandIconPicker.Attribute("HorizontalAlignment")?.Value);
        Assert.Null(brandIconPicker.Attribute("Width"));
        var periodInput = advanced
            .Descendants(avalonia + "NumericUpDown")
            .Single(control => control.Attribute("Classes")?.Value == "mobile-period");
        Assert.Null(periodInput.Attribute("Width"));
        var customIconButton = advanced
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountCustomIconButton");
        Assert.Equal(
            "{Binding ImportCustomIconCommand}",
            customIconButton.Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding HasNoInstalledBrandIconPack}",
            customIconButton.Attribute("IsVisible")?.Value);
        var noPackCallout = advanced
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "NoBrandIconPackCallout");
        Assert.Equal(
            "{Binding HasNoInstalledBrandIconPack}",
            noPackCallout.Attribute("IsVisible")?.Value);
        Assert.Contains(
            noPackCallout.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value
                == "{Binding NoBrandIconPackInstalledText}");
        Assert.Contains(
            noPackCallout.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value
                == "{Binding NoBrandIconPackInstructionsText}");
        Assert.DoesNotContain(
            advanced.Descendants(),
            element => element.Name.LocalName == "SearchableComboBox");
        var brandIconPickerButtonStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "Button.account-brand-icon-picker");
        Assert.Contains(
            brandIconPickerButtonStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Padding"
                && setter.Attribute("Value")?.Value == "12,8");
        Assert.DoesNotContain(
            advanced.Descendants(avalonia + "TextBlock"),
            control => control.Attribute("Text")?.Value == "{Binding OrText}");
        Assert.Contains(
            advanced.Descendants(avalonia + "TextBlock"),
            control => control.Attribute("Text")?.Value
                == "{Binding SelectedEditorCustomIconFileName}");
        Assert.Equal(
            2,
            advanced.Descendants(avalonia + "Border")
                .Count(border => border.Attribute("Height")?.Value == "1"
                    && border.Attribute("Background")?.Value
                        == "{DynamicResource BrushBorder}"));

        var editorHeader = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MobileAccountEditorHeader");
        Assert.DoesNotContain(editorHeader.Ancestors(), element => element.Name == avalonia + "ScrollViewer");
        Assert.Equal("{DynamicResource BrushWindowBackground}", editorHeader.Attribute("Background")?.Value);
        var saveButton = editorHeader
            .Descendants(avalonia + "Button")
            .Single(button => button.Attribute("Command")?.Value == "{Binding SaveAccountCommand}");
        var cancelButton = editorHeader
            .Descendants(avalonia + "Button")
            .Single(button => button.Attribute("Command")?.Value == "{Binding CancelEditCommand}");
        Assert.Contains(
            saveButton.Descendants(),
            icon => icon.Name.LocalName == "SymbolIcon"
                && icon.Attribute("Kind")?.Value == "Check"
                && icon.Attribute("Foreground")?.Value == "{DynamicResource BrushSuccess}");
        Assert.Contains(
            cancelButton.Descendants(),
            icon => icon.Name.LocalName == "SymbolIcon"
                && icon.Attribute("Kind")?.Value == "Close"
                && icon.Attribute("Foreground")?.Value == "{DynamicResource BrushDanger}");
        Assert.Single(
            document.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value == "{Binding SaveAccountCommand}");

        var exitModal = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountEditorExitModalOverlay");
        Assert.Equal(
            "{Binding IsAccountEditorExitConfirmationVisible}",
            exitModal.Attribute("IsVisible")?.Value);
        Assert.Contains(
            exitModal.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding SaveAccountAndNavigateBackCommand}");
        Assert.Contains(
            exitModal.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding DiscardAccountChangesCommand}");

        var brandIconModal = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconPickerModalOverlay");
        Assert.Equal("3", brandIconModal.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("104", brandIconModal.Attribute("Panel.ZIndex")?.Value);
        Assert.Equal(
            "{Binding IsAccountBrandIconPickerVisible}",
            brandIconModal.Attribute("IsVisible")?.Value);
        Assert.Empty(brandIconModal.Ancestors(avalonia + "Popup"));
        var modalSurface = Assert.Single(brandIconModal.Elements(avalonia + "Border"));
        Assert.Equal("520", modalSurface.Attribute("MaxWidth")?.Value);
        Assert.Equal("620", modalSurface.Attribute("MaxHeight")?.Value);
        Assert.Equal("Top", modalSurface.Attribute("VerticalAlignment")?.Value);
        var modalLayout = Assert.Single(modalSurface.Elements(avalonia + "Grid"));
        Assert.Equal("Auto,Auto,*", modalLayout.Attribute("RowDefinitions")?.Value);
        var modalSearch = brandIconModal
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconSearchBox");
        Assert.Equal(
            "{Binding AccountBrandIconSearchText, Mode=TwoWay}",
            modalSearch.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding HasSearchableBrandIcons}",
            modalSearch.Parent?.Attribute("IsVisible")?.Value);
        Assert.Null(modalSearch.Attribute("IsFocused"));
        var clearSearchButton = brandIconModal
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconClearSearchButton");
        Assert.Equal("False", clearSearchButton.Attribute("Focusable")?.Value);
        Assert.Equal("Center", clearSearchButton.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", clearSearchButton.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("0", clearSearchButton.Attribute("Padding")?.Value);
        var clearSearchIcon = Assert.Single(
            clearSearchButton.Elements(controls + "SymbolIcon"));
        Assert.Equal("Close", clearSearchIcon.Attribute("Kind")?.Value);
        Assert.Null(clearSearchIcon.Attribute("IconSize"));
        Assert.DoesNotContain(
            brandIconModal.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value
                == "{Binding NoBrandIconPackInstalledText}");
        var resultsScroller = brandIconModal
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconResultsScroller");
        Assert.Equal(
            "Auto",
            resultsScroller.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "{Binding AccountBrandIconResultsMaxHeight}",
            resultsScroller.Parent?.Attribute("MaxHeight")?.Value);
        Assert.Single(resultsScroller.Descendants(avalonia + "VirtualizingStackPanel"));
        var resultItemStyles = resultsScroller
            .Descendants(avalonia + "Style")
            .Where(style => style.Attribute("Selector")?.Value?.StartsWith(
                "ListBoxItem",
                StringComparison.Ordinal) == true)
            .ToArray();
        var neutralResultItemStyle = resultItemStyles.Single(style =>
            style.Attribute("Selector")?.Value == "ListBoxItem");
        Assert.Single(
            neutralResultItemStyle.Descendants(avalonia + "ControlTemplate"));
        Assert.Contains(
            resultItemStyles,
            style => style.Attribute("Selector")?.Value == "ListBoxItem:selected"
                && style.Elements(avalonia + "Setter").Any(setter =>
                    setter.Attribute("Property")?.Value == "Background"
                    && setter.Attribute("Value")?.Value == "Transparent"));
        Assert.Contains(
            resultItemStyles,
            style => style.Attribute("Selector")?.Value == "ListBoxItem:pressed"
                && style.Elements(avalonia + "Setter").Any(setter =>
                    setter.Attribute("Property")?.Value == "Background"
                    && setter.Attribute("Value")?.Value == "Transparent"));
        var brandChoice = Assert.Single(
            resultsScroller.Descendants(avalonia + "Button"),
            button => button.Attribute("Classes")?.Value == "brand-icon-choice");
        Assert.Equal(
            "{Binding IsBrandEntry}",
            brandChoice.Attribute("Classes.brand-entry")?.Value);
        Assert.Equal(
            "{Binding IsPickerAction}",
            brandChoice.Attribute("Classes.picker-action")?.Value);
        Assert.Equal(
            "Center",
            brandChoice
                .Descendants(avalonia + "TextBlock")
                .Single()
                .Attribute("VerticalAlignment")?.Value);
        var brandChoiceStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "Button.brand-icon-choice");
        Assert.Single(brandChoiceStyle.Descendants(avalonia + "ControlTemplate"));
        var pressedChoiceStyles = document
            .Descendants(avalonia + "Style")
            .Where(style => style.Attribute("Selector")?.Value is
                "Button.brand-icon-choice.picker-action:pressed"
                or "Button.brand-icon-choice.brand-entry:pressed")
            .ToArray();
        Assert.Equal(2, pressedChoiceStyles.Length);
        Assert.DoesNotContain(
            pressedChoiceStyles.SelectMany(style => style.Elements(avalonia + "Setter")),
            setter => setter.Attribute("Value")?.Value
                == "{DynamicResource BrushSurfacePressed}");
        Assert.DoesNotContain(
            brandIconModal.Descendants(avalonia + "ItemsControl"),
            element => element.Attribute("ItemsSource")?.Value
                == "{Binding FilteredEditorBrandIconOptions}");
        Assert.DoesNotContain(
            brandIconModal.Descendants(avalonia + "Button"),
            button => button.Attribute("Content")?.Value == "{Binding CancelText}");
    }

    [Fact]
    public void Settings_UsesCategoryPagesWithLanguagePickerAndMiscLogging()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var categories = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsSettingsCategoryListVisible}");
        var categoryButtons = categories.Elements(avalonia + "Button").ToArray();
        Assert.Equal(7, categoryButtons.Length);
        Assert.Equal(
            ["Palette", "ImageStack", "Vault", "BackupRestore", "Import", "Diagnostics", "Help"],
            categoryButtons.Select(button => button
                .Descendants()
                .Single(element => element.Name.LocalName == "SymbolIcon")
                .Attribute("Kind")?.Value));
        Assert.Equal(
            "{Binding ShowFaqSettingsCommand}",
            categoryButtons[^1].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding ShowMiscSettingsCommand}",
            categoryButtons[^2].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding ShowImportExportSettingsCommand}",
            categoryButtons[^3].Attribute("Command")?.Value);

        var appearance = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsAppearanceSettingsVisible}");
        var languagePicker = Assert.Single(appearance.Descendants(avalonia + "ComboBox"));
        Assert.Equal("{Binding Languages}", languagePicker.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding SelectedLanguage, Mode=TwoWay}",
            languagePicker.Attribute("SelectedItem")?.Value);
        Assert.Equal("88", languagePicker.Attribute("Width")?.Value);
        XNamespace controls = "using:TOTP.Avalonia.Shared.Controls";
        Assert.Equal(2, languagePicker.Descendants(controls + "LanguageFlagImage").Count());
        Assert.All(
            languagePicker.Descendants(controls + "LanguageFlagImage"),
            image =>
            {
                Assert.Equal("{Binding CultureName}", image.Attribute("CultureName")?.Value);
                Assert.Equal("40", image.Attribute("Width")?.Value);
                Assert.Equal("30", image.Attribute("Height")?.Value);
            });
        Assert.Contains(
            languagePicker.Descendants(avalonia + "ComboBox.SelectionBoxItemTemplate"),
            _ => true);
        Assert.Contains(
            appearance.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding ThemeText}");

        var security = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsSecuritySettingsVisible}");
        Assert.Equal("12", security.Attribute("Spacing")?.Value);
        Assert.DoesNotContain(
            document.Descendants(avalonia + "Border"),
            element => element.Attribute("IsVisible")?.Value
                == "{Binding IsSecuritySettingsVisible}");
        Assert.DoesNotContain(
            security.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding SecurityText}");
        var securitySectionPanels = new[]
        {
            "AppLockPanel",
            "UnlockMethodPanel"
        }.Select(name => security
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == name))
            .ToArray();
        Assert.All(
            securitySectionPanels,
            panel => Assert.Equal("1", panel.Attribute("BorderThickness")?.Value));
        Assert.Contains(
            security.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding ChangeMasterPasswordCommand}");

        var misc = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsMiscSettingsVisible}");
        var loggingPicker = Assert.Single(misc.Descendants(avalonia + "ComboBox"));
        Assert.Equal("{Binding LogLevels}", loggingPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding MinimumLogLevel, Mode=TwoWay}",
            loggingPicker.Attribute("SelectedItem")?.Value);

        var imports = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsImportExportSettingsVisible}");
        Assert.Null(imports.Attribute("Classes"));
        Assert.DoesNotContain(
            imports.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding ImportSectionText}");
        var googleImportPanel = imports
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GoogleAuthenticatorImportPanel");
        var otherFormatsPanel = imports
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "OtherFormatsImportPanel");
        Assert.All(
            new[] { googleImportPanel, otherFormatsPanel },
            panel => Assert.Equal("1", panel.Attribute("BorderThickness")?.Value));
        var importButtons = new[]
        {
            googleImportPanel.Descendants(avalonia + "Button").Single(button =>
                button.Attribute(xaml + "Name")?.Value == "GoogleAuthenticatorImportButton"),
            otherFormatsPanel.Descendants(avalonia + "Button").Single(button =>
                button.Attribute(xaml + "Name")?.Value == "OtherFormatsImportButton")
        };
        Assert.All(importButtons, button =>
        {
            Assert.Equal("wide secondary", button.Attribute("Classes")?.Value);
            var label = Assert.Single(
                button.Elements(),
                element => element.Name.LocalName == "SymbolIconLabel");
            Assert.Equal("{Binding ImportSectionText}", label.Attribute("Text")?.Value);
        });
        Assert.Equal(
            ["QrCode", "Import"],
            importButtons.Select(button => button.Elements().Single().Attribute("Kind")?.Value));
        var diagnosticExport = Assert.Single(misc.Descendants(avalonia + "Button"));
        Assert.Equal(
            "{Binding ExportDiagnosticLogCommand}",
            diagnosticExport.Attribute("Command")?.Value);
        var diagnosticLabel = Assert.Single(
            diagnosticExport.Elements(),
            element => element.Name.LocalName == "SymbolIconLabel");
        Assert.Equal("Export", diagnosticLabel.Attribute("Kind")?.Value);
        Assert.Equal(
            [
                "{Binding ImportFormatAegisText}",
                "{Binding ImportFormatTwoFasText}",
                "{Binding ImportFormatOtpAuthText}"
            ],
            otherFormatsPanel
                .Descendants(avalonia + "TextBlock")
                .Select(text => text.Attribute("Text")?.Value)
                .Where(value => value?.StartsWith("{Binding ImportFormat", StringComparison.Ordinal) == true));
        var faqLink = otherFormatsPanel.Descendants(avalonia + "Button").Single(button =>
            button.Attribute("Command")?.Value == "{Binding ShowImportFormatsFaqCommand}");
        Assert.Equal("link", faqLink.Attribute("Classes")?.Value);

        var backup = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsBackupSettingsVisible}");
        Assert.Null(backup.Attribute("Classes"));
        Assert.DoesNotContain(
            backup.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding BackupDescription}");
        var backupExportPanel = backup.Descendants(avalonia + "Border").Single(element =>
            element.Attribute(xaml + "Name")?.Value == "BackupExportPanel");
        var backupRestorePanel = backup.Descendants(avalonia + "Border").Single(element =>
            element.Attribute(xaml + "Name")?.Value == "BackupRestorePanel");
        Assert.Equal("{Binding BackupSectionText}", backupExportPanel.Descendants(avalonia + "TextBlock").First().Attribute("Text")?.Value);
        Assert.Equal("{Binding RestoreSectionText}", backupRestorePanel.Descendants(avalonia + "TextBlock").First().Attribute("Text")?.Value);

        var settingsRoot = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("Classes")?.Value == "settings-root");
        Assert.Equal("{Binding IsSettingsVisible}", settingsRoot.Attribute("IsVisible")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "Style"),
            style => style.Attribute("Selector")?.Value == "StackPanel.settings-root Button.wide");
        var categoryStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "StackPanel.settings-root Button.wide.settings-category");
        Assert.Contains(
            categoryStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value == "{DynamicResource BrushSurface}");
        var categoryBaseStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value == "Button.settings-category");
        Assert.Contains(
            categoryBaseStyle.Descendants(avalonia + "ControlTemplate"),
            template => template.Descendants(avalonia + "Border").Any(border =>
                border.Attribute("Background")?.Value == "{TemplateBinding Background}"));
        var pressedCategoryStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "StackPanel.settings-root Button.wide.settings-category:pressed");
        Assert.Contains(
            pressedCategoryStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value
                    == "{DynamicResource BrushSettingsCategoryPressed}");
        var app = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileApp.axaml"));
        Assert.Equal("#EDEDED", ThemeResourceColor(
            app,
            "Light",
            "BrushSettingsCategoryPressed"));
        Assert.Equal("#35547F", ThemeResourceColor(
            app,
            "Dark",
            "BrushSettingsCategoryPressed"));

        var faq = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsFaqSettingsVisible}");
        var faqSections = faq
            .Descendants(avalonia + "Expander")
            .Where(section => section.Attribute("Classes")?.Value == "mobile-faq-section")
            .ToArray();
        Assert.Equal(2, faqSections.Length);
        Assert.All(
            faqSections,
            section => Assert.Equal(
                "FaqSectionExpanded",
                section.Attribute("Expanded")?.Value));
        Assert.Equal(
            ["{Binding FaqImportIconPacksQuestionText}", "{Binding FaqImportFormatsQuestionText}"],
            faqSections.Select(section => section
                .Descendants(avalonia + "TextBlock")
                .First()
                .Attribute("Text")?.Value));
        Assert.Equal(
            ["Folder", "QrCode"],
            faqSections.Select(section => section
                .Element(avalonia + "Expander.Header")!
                .Descendants()
                .Single(element => element.Name.LocalName == "SymbolIcon")
                .Attribute("Kind")?.Value));
        Assert.Equal(
            [
                "https://github.com/Legends/otp-harbor-icon-pack-builder/releases/latest",
                "https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md",
                "https://github.com/aegis-icons/aegis-icons#disclaimer",
                "https://github.com/homarr-labs/dashboard-icons#legal"
            ],
            faqSections[0]
                .Descendants(avalonia + "HyperlinkButton")
                .Select(link => link.Attribute("NavigateUri")?.Value));
        var iconPackSteps = faqSections[0]
            .Descendants(avalonia + "Grid")
            .Where(grid => grid.Elements(avalonia + "TextBlock").Any(text =>
                text.Attribute("Text")?.Value is "1." or "2." or "3."))
            .ToArray();
        Assert.Equal(3, iconPackSteps.Length);
        Assert.Equal(
            [
                "{Binding FaqImportIconPacksStepDownloadText}",
                "{Binding FaqImportIconPacksStepBuildText}",
                "{Binding FaqImportIconPacksStepImportText}"
            ],
            iconPackSteps.Select(step => step
                .Descendants(avalonia + "TextBlock")
                .Single(text => text.Attribute("Text")?.Value?.StartsWith(
                    "{Binding FaqImportIconPacksStep",
                    StringComparison.Ordinal) == true)
                .Attribute("Text")?.Value));
        Assert.Contains(
            faqSections[0].Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value
                == "{Binding FaqImportIconPacksCustomIconTipText}");
        Assert.Contains(
            faqSections[1].Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{Binding FaqImportFormatsAegisDescriptionText}");
        Assert.Contains(
            faqSections[1].Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{Binding FaqImportFormatsSupportedText}");
        var importFormatSections = faqSections[1]
            .Descendants(avalonia + "Expander")
            .Where(section => section.Attribute("Classes")?.Value == "mobile-faq-subsection")
            .ToArray();
        Assert.Equal(3, importFormatSections.Length);
        Assert.All(
            importFormatSections,
            section => Assert.Equal(
                "FaqImportFormatSectionExpanded",
                section.Attribute("Expanded")?.Value));
        Assert.Equal(
            [
                "{Binding FaqImportFormatsAegisTitleText}",
                "{Binding FaqImportFormatsTwoFasTitleText}",
                "{Binding FaqImportFormatsOtpAuthTitleText}"
            ],
            importFormatSections.Select(section => section
                .Descendants(avalonia + "TextBlock")
                .First()
                .Attribute("Text")?.Value));
        foreach (var state in new[] { "pointerover", "pressed" })
        {
            var stateStyle = document
                .Descendants(avalonia + "Style")
                .Single(style => style.Attribute("Selector")?.Value
                    == $"Expander.mobile-faq-subsection /template/ ToggleButton#ExpanderHeader:{state}");
            Assert.Contains(
                stateStyle.Elements(avalonia + "Setter"),
                setter => setter.Attribute("Property")?.Value == "Background"
                    && setter.Attribute("Value")?.Value == "Transparent");
        }
        Assert.All(
            faqSections,
            section =>
            {
                var card = Assert.IsType<XElement>(section.Parent);
                Assert.Equal(avalonia + "Border", card.Name);
                Assert.Equal("Stretch", card.Attribute("HorizontalAlignment")?.Value);
                Assert.Equal("{DynamicResource BrushFaqHeaderBackground}", card.Attribute("Background")?.Value);
                Assert.Equal("{DynamicResource ElevationCard}", card.Attribute("BoxShadow")?.Value);
                Assert.Null(card.Attribute("BorderBrush"));
                Assert.Equal("Stretch", section.Attribute("HorizontalAlignment")?.Value);
                var content = section.Elements(avalonia + "Border").Single();
                Assert.Equal("{DynamicResource BrushFaqContentBackground}", content.Attribute("Background")?.Value);
                Assert.Null(content.Attribute("BorderBrush"));
            });

        var backToTop = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "SettingsBackToTopButton");
        Assert.Equal("ScrollMainViewToTop", backToTop.Attribute("Click")?.Value);
        Assert.Equal("settings-back-to-top", backToTop.Attribute("Classes")?.Value);
        Assert.Equal("12", backToTop.Attribute("Padding")?.Value);
        var backToTopStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "Button.settings-back-to-top");
        var backToTopSetters = backToTopStyle
            .Elements(avalonia + "Setter")
            .ToDictionary(
                setter => setter.Attribute("Property")?.Value ?? string.Empty,
                setter => setter.Attribute("Value")?.Value ?? string.Empty);
        Assert.Equal("{DynamicResource BrushSurfaceAlternate}", backToTopSetters["Background"]);
        Assert.Equal("{DynamicResource BrushAccent}", backToTopSetters["BorderBrush"]);
        Assert.Equal("{DynamicResource BrushAccent}", backToTopSetters["Foreground"]);
        var backToTopIcon = Assert.Single(backToTop.Descendants(), element =>
            element.Name.LocalName == "SymbolIcon");
        Assert.Equal("22", backToTopIcon.Attribute("IconSize")?.Value);
        Assert.Equal("ArrowUp", backToTopIcon.Attribute("Kind")?.Value);
        Assert.Null(backToTopIcon.Attribute("Opacity"));
    }

    [Fact]
    public void LockScreen_UsesAccessibleFingerprintButtonForBiometricUnlock()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var button = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "FingerprintUnlockButton");

        Assert.Equal("{Binding BiometricUnlockCommand}", button.Attribute("Command")?.Value);
        Assert.Null(button.Attribute("Content"));
        Assert.Contains(
            "BiometricUnlockText",
            button.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal("92", button.Attribute("Width")?.Value);
        Assert.Equal("primary biometric-unlock", button.Attribute("Classes")?.Value);
        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalAlignment")?.Value);
        var fingerprint = button
            .Descendants()
            .Single(element => element.Name.LocalName == "FingerprintIcon");
        Assert.Equal("72", fingerprint.Attribute("IconSize")?.Value);

        var anchor = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "FingerprintUnlockAnchor");
        Assert.Equal("1", anchor.Attribute("Grid.Row")?.Value);
        Assert.Equal("5*,0,*", anchor.Attribute("RowDefinitions")?.Value);
        Assert.Equal(
            "{Binding IsFingerprintUnlockVisible}",
            anchor.Attribute("IsVisible")?.Value);
        Assert.Same(anchor, button.Parent);

        var artwork = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MobileLockScreenArtwork");
        Assert.Equal(
            "{DynamicResource BrushBiometricLockScreenArtwork}",
            artwork.Attribute("Background")?.Value);
        Assert.Equal("{Binding IsUnlockVisible}", artwork.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void LockScreen_DeviceCredentialButtonUsesDedicatedLocalizedTextBinding()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var button = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsDeviceCredentialUnlockVisible}");

        Assert.Equal("{Binding UnlockWithDevicePinText}", button.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("68", button.Attribute("MinHeight")?.Value);
        Assert.Equal("Stretch", button.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalContentAlignment")?.Value);
        var buttonText = button
            .Elements(avalonia + "TextBlock")
            .Single();
        Assert.Equal(
            "{Binding DeviceCredentialUnlockButtonText}",
            buttonText.Attribute("Text")?.Value);
        Assert.Equal("Center", buttonText.Attribute("TextAlignment")?.Value);
        Assert.Equal("Wrap", buttonText.Attribute("TextWrapping")?.Value);
    }

    [Fact]
    public void LongImports_ShowAnIndeterminateProgressOverlay()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var overlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsImportProgressVisible}");

        Assert.Equal("90", overlay.Attribute("Panel.ZIndex")?.Value);
        Assert.Contains(
            overlay.Descendants(avalonia + "ProgressBar"),
            progress => progress.Attribute("IsIndeterminate")?.Value == "True");
        Assert.Contains(
            overlay.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding ImportProgressText}");
    }

    [Fact]
    public void AccountSearch_ClearButtonUsesCenteredVectorIcon()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var searchBox = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountSearchBox");
        var clearButton = searchBox.Parent?
            .Elements(avalonia + "Button")
            .Single();

        Assert.NotNull(clearButton);
        Assert.Equal("{Binding ClearSearchCommand}", clearButton.Attribute("Command")?.Value);
        Assert.Equal("False", clearButton.Attribute("Focusable")?.Value);
        Assert.Equal(
            "KeepAccountSearchFocusedAfterClear",
            clearButton.Attribute("Click")?.Value);
        Assert.Equal("Center", clearButton.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", clearButton.Attribute("VerticalContentAlignment")?.Value);
        var closeIcon = clearButton
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon");
        Assert.Equal("Close", closeIcon.Attribute("Kind")?.Value);
        Assert.Equal("18", closeIcon.Attribute("IconSize")?.Value);
    }

    [Fact]
    public void AccountSearch_HostCanReceiveFocusWhenTheUserTapsOutsideTheSearchBox()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var root = document.Root;
        Assert.NotNull(root);
        Assert.Equal("Root", root.Attribute(xaml + "Name")?.Value);
        Assert.Equal("True", root.Attribute("Focusable")?.Value);

        var searchBox = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountSearchBox");
        Assert.NotNull(searchBox);
    }

    [Fact]
    public void AccountList_BuffersVirtualizedRowsForSmoothMobileScrolling()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var accountList = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountList");
        var itemsPanel = accountList
            .Descendants(avalonia + "VirtualizingStackPanel")
            .Single();

        Assert.Equal("1", itemsPanel.Attribute("CacheLength")?.Value);
    }

    [Fact]
    public void MainShell_DefinesCompactPhoneBreakpointsWithoutShrinkingTouchHeight()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var shell = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute("Container.Name")?.Value == "MobileShell");
        Assert.Equal("Width", shell.Attribute("Container.Sizing")?.Value);

        var compactQuery = document
            .Descendants(avalonia + "ContainerQuery")
            .Single(element => element.Attribute("Query")?.Value == "max-width:380");
        Assert.Contains(
            compactQuery.Descendants(avalonia + "Style"),
            style => style.Attribute("Selector")?.Value == "Grid#MobileMainContent");
        var compactActionStyle = compactQuery
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value == "Button.account-toolbar-action");
        Assert.Contains(
            compactActionStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "MinWidth"
                && setter.Attribute("Value")?.Value == "48");

        var toolbar = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountToolbar");
        Assert.Equal(
            4,
            toolbar.Elements(avalonia + "Button").Count(button =>
                button.Attribute("Classes")?.Value.Contains(
                    "account-toolbar-action",
                    StringComparison.Ordinal) == true));
        var searchPanel = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountSearchPanel");
        Assert.Equal("0,-4,0,8", searchPanel.Attribute("Margin")?.Value);
    }

    [Fact]
    public void AccountRows_ExposeFavoriteActionAndCopyOnRowTap()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var favoriteButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountFavoriteButton");
        Assert.Equal("ToggleAccountFavorite", favoriteButton.Attribute("Click")?.Value);
        Assert.Contains(
            "FavoriteActionText",
            favoriteButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name").Value);

        var codeDisplay = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountCodeDisplay");
        Assert.Null(codeDisplay.Attribute("Click"));
        var copyConfirmation = codeDisplay
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value.Contains(
                "CopyConfirmation",
                StringComparison.Ordinal) == true);
        Assert.Equal("16", copyConfirmation.Parent?.Attribute("MinHeight")?.Value);
        Assert.Equal("NoWrap", copyConfirmation.Attribute("TextWrapping")?.Value);
        Assert.Null(copyConfirmation.Attribute("IsVisible"));
        Assert.Contains(
            copyConfirmation.Parent!.Elements(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding DisplayNextCode}"
                && text.Attribute("IsVisible")?.Value == "{Binding HasVisibleNextCode}"
                && text.Attribute("FontSize")?.Value == "16");

        var countdown = document
            .Descendants(avalonia + "ProgressBar")
            .Single(element => element.Attribute("Classes")?.Value == "account-countdown");
        Assert.Equal(
            "{Binding IsCodeLoading}",
            countdown.Attribute("IsIndeterminate")?.Value);

        var groupItems = document
            .Descendants(avalonia + "ItemsControl")
            .Single(element => element.Attribute("ItemsSource")?.Value == "{Binding Groups}");
        var groupButton = groupItems
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "account-group",
                StringComparison.Ordinal) == true);
        Assert.Equal("{Binding SelectCommand}", groupButton.Attribute("Command")?.Value);
        var groupCard = groupButton
            .Ancestors(avalonia + "Border")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "group-card",
                StringComparison.Ordinal) == true);
        Assert.Equal("{Binding Background}", groupCard.Attribute("Background")?.Value);
        Assert.Contains("group-card", groupCard.Attribute("Classes")?.Value);
        Assert.Equal("{Binding IsSelected}", groupCard.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Classes.selected").Value);
        var editGroupButton = groupCard
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding EditCommand}");
        Assert.Equal("1", editGroupButton.Attribute("Grid.Column")?.Value);
        var groupName = groupButton
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding Name}");
        Assert.Equal("CharacterEllipsis", groupName.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", groupName.Attribute("TextWrapping")?.Value);

        var favoriteName = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding FavoritesText}"
                && element.Ancestors(avalonia + "ToggleButton").Any());
        Assert.Equal("CharacterEllipsis", favoriteName.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", favoriteName.Attribute("TextWrapping")?.Value);

        var groupsStrip = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountGroupsStrip");
        Assert.Equal("{Binding HasAccountNavigationCards}", groupsStrip.Attribute("IsVisible")?.Value);
        var groupsFallbackPresenter = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountGroupsFallbackPresenter");
        Assert.Equal("2", groupsFallbackPresenter.Attribute("Grid.Row")?.Value);
        Assert.Equal("56", groupsFallbackPresenter.Attribute("MinHeight")?.Value);
        Assert.Equal("0,2,0,6", groupsFallbackPresenter.Attribute("Margin")?.Value);
        Assert.Equal("Center", groupsFallbackPresenter.Attribute("VerticalAlignment")?.Value);
        Assert.Equal(
            "{Binding HasAccountNavigationCards}",
            groupsFallbackPresenter.Attribute("IsVisible")?.Value);

        var createGroupButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding BeginAddGroupCommand}");
        Assert.Equal("2", createGroupButton.Attribute("Grid.Column")?.Value);
        Assert.Contains("icon-action", createGroupButton.Attribute("Classes")?.Value);
        Assert.Contains(
            createGroupButton.Descendants(),
            element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "FolderAdd");
        var cameraIcon = document
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "Camera");
        Assert.Equal("0,2,0,0", cameraIcon.Attribute("Margin")?.Value);

        var accountCountSummary = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding SearchResultSummary}");
        var accountCountFooter = accountCountSummary.Parent;
        Assert.NotNull(accountCountFooter);
        Assert.Equal("AccountCountFooter", accountCountFooter.Attribute(xaml + "Name")?.Value);
        var accountListFooter = accountCountFooter.Parent;
        Assert.NotNull(accountListFooter);
        Assert.Equal("AccountListFooter", accountListFooter.Attribute(xaml + "Name")?.Value);
        Assert.Equal("2", accountListFooter.Attribute("Grid.Row")?.Value);
        Assert.Equal("40", accountListFooter.Attribute("Height")?.Value);
        Assert.Equal("{Binding IsAccountListVisible}", accountListFooter.Attribute("IsVisible")?.Value);
        var accountListNotification = accountListFooter
            .Elements()
            .Single(element => element.Name.LocalName == "NotificationBanner");
        Assert.Equal("{Binding NotificationText}", accountListNotification.Attribute("Text")?.Value);
        Assert.Equal("Stretch", accountListNotification.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Center", accountCountSummary.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("11", accountCountSummary.Attribute("FontSize")?.Value);
        Assert.DoesNotContain(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding AccountSwipeHintText}");

        var accountList = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "mobile-accounts",
                StringComparison.Ordinal) == true);
        Assert.Equal("4", accountList.Attribute("Grid.Row")?.Value);
        Assert.Equal("-14,0", accountList.Attribute("Margin")?.Value);
        Assert.Equal(
            "Disabled",
            accountList.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            "False",
            accountList.Attribute("ScrollViewer.IsScrollChainingEnabled")?.Value);
        Assert.Single(accountList.Descendants(avalonia + "VirtualizingStackPanel"));
        var accountContainer = favoriteButton
            .Ancestors(avalonia + "Border")
            .Single(element => element.Attribute("Margin")?.Value == "2,4");
        Assert.NotNull(accountContainer);

        var swipeRow = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("SwipeGesture")?.Value == "TrackAccountSwipe");
        Assert.Equal("CompleteAccountSwipe", swipeRow.Attribute("SwipeGestureEnded")?.Value);
        Assert.Equal("CopyAccountCode", swipeRow.Attribute("Tapped")?.Value);
        Assert.Contains(
            swipeRow.Descendants(),
            element => element.Name.LocalName == "SwipeGestureRecognizer"
                && element.Attribute("CanHorizontallySwipe")?.Value == "True"
                && element.Attribute("Threshold")?.Value == "24");
    }

    [Fact]
    public void GroupEditor_ExposesLocalizedCreateEditAndDeleteWorkflow()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var nameBox = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupNameBox");
        Assert.Equal("64", nameBox.Attribute("MaxLength")?.Value);
        Assert.Contains("GroupNameText", nameBox.Attribute("PlaceholderText")?.Value);

        var colorPicker = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupColorPicker");
        Assert.Equal("{Binding GroupColorOptions}", colorPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding SelectedGroupColor, Mode=TwoWay}", colorPicker.Attribute("SelectedItem")?.Value);

        var nativeGroupStrip = document
            .Descendants(avalonia + "ContentControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "NativeAccountGroupsPresenter");
        Assert.Equal(
            "{Binding IsNativeAccountGroupsVisible}",
            nativeGroupStrip.Attribute("IsVisible")?.Value);
        Assert.Equal("56", nativeGroupStrip.Attribute("MinHeight")?.Value);
        Assert.Equal("0,2,0,6", nativeGroupStrip.Attribute("Margin")?.Value);

        var accountPicker = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupAccountPicker");
        Assert.Equal("{Binding GroupEditorAccounts}", accountPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal("220", accountPicker.Parent?.Attribute("Height")?.Value);
        Assert.Contains(
            accountPicker.Descendants(avalonia + "VirtualizingStackPanel"),
            element => element.Attribute("CacheLength")?.Value == "0.5");
        var nativeAccountPicker = document
            .Descendants(avalonia + "ContentControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "NativeGroupAccountPickerPresenter");
        Assert.Same(accountPicker.Parent, nativeAccountPicker.Parent);
        Assert.Equal("False", nativeAccountPicker.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal(
            "{Binding !IsDeleteGroupConfirmationVisible}",
            nativeAccountPicker.Attribute("IsVisible")?.Value);
        var accountSearch = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupAccountSearchBox");
        Assert.Equal(
            "{Binding GroupEditorSearchText, Mode=TwoWay}",
            accountSearch.Attribute("Text")?.Value);
        var clearGroupSearchButton = accountSearch.Parent?
            .Elements(avalonia + "Button")
            .Single();
        Assert.Equal(
            "{Binding ClearGroupEditorSearchCommand}",
            clearGroupSearchButton?.Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding HasGroupEditorSearchText}",
            clearGroupSearchButton?.Attribute("IsVisible")?.Value);
        Assert.Equal("False", clearGroupSearchButton?.Attribute("Focusable")?.Value);
        Assert.Equal(
            "KeepGroupAccountSearchFocusedAfterClear",
            clearGroupSearchButton?.Attribute("Click")?.Value);
        Assert.Equal("Hidden", accountPicker.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal("False", accountPicker.Attribute("ScrollViewer.IsScrollChainingEnabled")?.Value);
        var accountSelection = accountPicker
            .Descendants(avalonia + "CheckBox")
            .Single(element => element.Attribute("IsChecked")?.Value.Contains(
                "IsSelected",
                StringComparison.Ordinal) == true);
        Assert.Equal("Center", accountSelection.Attribute("VerticalContentAlignment")?.Value);
        var editorHeader = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MobileGroupEditorHeader");
        Assert.DoesNotContain(editorHeader.Ancestors(), element => element.Name == avalonia + "ScrollViewer");
        var saveButton = editorHeader
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding SaveGroupCommand}");
        var cancelButton = editorHeader
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding CancelGroupEditCommand}");
        Assert.Contains(
            saveButton.Descendants(),
            icon => icon.Name.LocalName == "SymbolIcon"
                && icon.Attribute("Kind")?.Value == "Check"
                && icon.Attribute("Foreground")?.Value == "{DynamicResource BrushSuccess}");
        Assert.Contains(
            cancelButton.Descendants(),
            icon => icon.Name.LocalName == "SymbolIcon"
                && icon.Attribute("Kind")?.Value == "Close"
                && icon.Attribute("Foreground")?.Value == "{DynamicResource BrushDanger}");
        Assert.Single(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value == "{Binding SaveGroupCommand}");
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value == "{Binding BeginDeleteGroupCommand}");
        var deleteOverlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GroupDeleteModalOverlay");
        Assert.Equal("3", deleteOverlay.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("102", deleteOverlay.Attribute("Panel.ZIndex")?.Value);
        Assert.Equal(
            "{Binding IsDeleteGroupConfirmationVisible}",
            deleteOverlay.Attribute("IsVisible")?.Value);
        var deleteDialog = deleteOverlay.Elements(avalonia + "Border").Single();
        Assert.Null(deleteDialog.Attribute("BorderBrush"));
        Assert.Null(deleteDialog.Attribute("BorderThickness"));
        Assert.Contains(
            deleteOverlay.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value
                == "{Binding ConfirmDeleteGroupCommand}");

        var noSearchResults = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding HasNoSearchResults}");
        Assert.Equal("Center", noSearchResults.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", noSearchResults.Attribute("TextAlignment")?.Value);
        Assert.Equal("0,24,0,0", noSearchResults.Attribute("Margin")?.Value);
    }

    [Fact]
    public void FavoriteFilter_UsesPastelYellowPaletteWithoutDisabledFlicker()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");
        var view = XDocument.Load(Path.Combine(fixtureDirectory, "MobileMainView.axaml"));
        var favoriteFilter = view
            .Descendants()
            .Single(element => element.Name.LocalName == "ToggleButton"
                && element.Attribute("Classes")?.Value == "favorites-filter");
        Assert.NotNull(favoriteFilter);
        var favoriteIcon = favoriteFilter
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "Favorite");
        Assert.Equal(
            "{DynamicResource BrushFavoriteFilterIcon}",
            favoriteIcon.Attribute("Foreground")?.Value);
        var favoriteContent = favoriteFilter
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        Assert.Equal("Auto,*,Auto", favoriteContent.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("7", favoriteContent.Attribute("ColumnSpacing")?.Value);
        Assert.Equal(2, favoriteContent.Elements().Count(element => element.Name.LocalName == "TextBlock"));
        Assert.DoesNotContain(
            favoriteContent.Elements(),
            element => element.Name.LocalName == "StackPanel");

        var app = XDocument.Load(Path.Combine(fixtureDirectory, "MobileApp.axaml"));
        Assert.Equal("#FFF4C2", ThemeResourceColor(app, "Light", "BrushFavoriteFilterBackground"));
        Assert.Equal("#FFE89A", ThemeResourceColor(app, "Light", "BrushFavoriteFilterBackgroundActive"));
        Assert.Equal("#FABD62", ThemeResourceColor(app, "Light", "BrushFavoriteFilterIcon"));
        Assert.Equal("#40361F", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterBackground"));
        Assert.Equal("#514526", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterBackgroundActive"));
        Assert.Equal("#9CA3AF", ThemeResourceColor(app, "Light", "BrushFavoriteFilterForeground"));
        Assert.Equal("#E5E7EB", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterForeground"));
        Assert.Equal("#FFD58A", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterIcon"));

        var stableDisabledSelectors = app
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Where(element => element.Attribute("Selector")?.Value is
                "Button.icon-action:disabled" or
                "ToggleButton.favorites-filter:disabled")
            .ToArray();
        Assert.Equal(2, stableDisabledSelectors.Length);
        Assert.All(stableDisabledSelectors, style => Assert.Contains(
            style.Elements(),
            setter => setter.Attribute("Property")?.Value == "Opacity"
                && setter.Attribute("Value")?.Value == "1"));
        var disabledIconActionStyle = stableDisabledSelectors.Single(
            style => style.Attribute("Selector")?.Value == "Button.icon-action:disabled");
        var disabledIconActionSetters = disabledIconActionStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? string.Empty,
                element => element.Attribute("Value")?.Value ?? string.Empty);
        Assert.Equal("Transparent", disabledIconActionSetters["Background"]);
        Assert.Equal("Transparent", disabledIconActionSetters["BorderBrush"]);
        Assert.Equal(
            "{DynamicResource BrushDisabledIcon}",
            disabledIconActionSetters["Foreground"]);
        var sharedStyles = XDocument.Load(Path.Combine(fixtureDirectory, "SharedStyles.axaml"));
        Assert.Equal("#9CA3AF", ThemeResourceColor(sharedStyles, "Light", "BrushDisabledIcon"));
        Assert.Equal("#A7B0BE", ThemeResourceColor(sharedStyles, "Dark", "BrushDisabledIcon"));
        var iconActionStyle = app
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.icon-action");
        var iconActionTemplate = iconActionStyle
            .Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Template");
        Assert.Contains(
            iconActionTemplate.Descendants(),
            element => element.Attribute("Name")?.Value == "IconActionBorder");
        Assert.DoesNotContain(
            iconActionTemplate.Descendants(),
            element => element.Attribute("Name")?.Value == "PART_Border");
        var disabledSymbolStyle = app
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value ==
                    "Button.icon-action:disabled controls|SymbolIcon");
        Assert.Contains(
            disabledSymbolStyle.Elements(),
            setter => setter.Attribute("Property")?.Value == "Foreground"
                && setter.Attribute("Value")?.Value == "{DynamicResource BrushDisabledIcon}");

        var selectedGroupStyle = app
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.group-card.selected");
        Assert.Contains(
            selectedGroupStyle.Elements(),
            setter => setter.Attribute("Property")?.Value == "BorderThickness"
                && setter.Attribute("Value")?.Value == "1");
    }

    private static string ThemeResourceColor(
        XDocument document,
        string theme,
        string resourceKey)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var themeDictionary = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary"
                && element.Attribute(xaml + "Key")?.Value == theme);

        return themeDictionary
            .Elements()
            .Single(element => element.Attribute(xaml + "Key")?.Value == resourceKey)
            .Attribute("Color")!
            .Value;
    }
}
