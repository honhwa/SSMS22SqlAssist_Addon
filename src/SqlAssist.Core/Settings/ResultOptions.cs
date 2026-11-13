using SqlAssist.Core.Results;

namespace SqlAssist.Core.Settings
{
    /// <summary>How results grid › Generate Script As writes its scripts.</summary>
    public sealed class ResultOptions
    {
        /// <summary>INSERT with an IDENTITY column selected: keep its values (SET IDENTITY_INSERT) or leave the column out.</summary>
        public InsertIdentityMode InsertIdentity { get; set; } = InsertIdentityMode.KeepValues;
    }
}
