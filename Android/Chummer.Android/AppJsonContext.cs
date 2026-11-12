// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Chummer.AndroidApp;

[JsonSerializable(typeof(WorkbenchActivity.InitiativeState))]
internal partial class AppJsonContext : JsonSerializerContext { }
