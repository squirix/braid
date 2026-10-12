using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Braid.Examples.CacheCasRace;

/// <summary>Describes the outcome of a compare-and-set attempt on a versioned cell.</summary>
public enum CasResult
{
    /// <summary>The value was updated successfully.</summary>
    Ok = 0,

    /// <summary>The expected version did not match the current version.</summary>
    VersionMismatch = 1,
}
