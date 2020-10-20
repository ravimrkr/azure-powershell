namespace Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview
{
    using static Microsoft.Azure.PowerShell.Cmdlets.Migrate.Runtime.Extensions;

    /// <summary>List of API operations.</summary>
    public partial class OperationResultList :
        Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperationResultList,
        Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperationResultListInternal
    {

        /// <summary>Backing field for <see cref="Value" /> property.</summary>
        private Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperation[] _value;

        /// <summary>List of operations.</summary>
        [Microsoft.Azure.PowerShell.Cmdlets.Migrate.Origin(Microsoft.Azure.PowerShell.Cmdlets.Migrate.PropertyOrigin.Owned)]
        public Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperation[] Value { get => this._value; set => this._value = value; }

        /// <summary>Creates an new <see cref="OperationResultList" /> instance.</summary>
        public OperationResultList()
        {

        }
    }
    /// List of API operations.
    public partial interface IOperationResultList :
        Microsoft.Azure.PowerShell.Cmdlets.Migrate.Runtime.IJsonSerializable
    {
        /// <summary>List of operations.</summary>
        [Microsoft.Azure.PowerShell.Cmdlets.Migrate.Runtime.Info(
        Required = false,
        ReadOnly = false,
        Description = @"List of operations.",
        SerializedName = @"value",
        PossibleTypes = new [] { typeof(Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperation) })]
        Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperation[] Value { get; set; }

    }
    /// List of API operations.
    internal partial interface IOperationResultListInternal

    {
        /// <summary>List of operations.</summary>
        Microsoft.Azure.PowerShell.Cmdlets.Migrate.Models.Api20180901Preview.IOperation[] Value { get; set; }

    }
}