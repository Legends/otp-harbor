using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOTP.Core.Enums;

namespace TOTP.Core.Services.Interfaces;

public interface ILogSwitchService
{
    public AppLogLevel MinimumLevel { get; }

    /// <summary>
    /// Gets a value indicating whether a command-line interface (CLI) override is currently active.
    /// </summary>
    bool IsCliOverrideActive { get; set; }

    /// <summary>
    /// Updates the minimum logging level globally.
    /// </summary>
    void SetLevel(AppLogLevel level);

    /// <summary>
    /// Retrieves the current minimum logging level.
    /// </summary>
    AppLogLevel GetLevel();
}
