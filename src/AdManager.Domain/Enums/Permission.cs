namespace AdManager.Domain.Enums;

/// <summary>Атомарные операции, делегируемые техникам (по образцу ADManager Plus).</summary>
public enum Permission
{
    // Users
    ResetPassword,
    UnlockAccount,
    EnableDisableUser,
    CreateUser,
    DeleteUser,
    ModifyAttributes,
    MoveObject,
    ManageGroupMembership,
    Rename,
    SetAccountOptions,

    // Groups
    CreateGroup,

    // Computers
    CreateComputer,
    ManageComputer,

    // Organizational Units
    CreateOu,
    ManageOu,

    // Contacts
    CreateContact,
    ManageContact,

    // Generic delete (group/computer/OU/contact)
    DeleteObject,

    // Exchange
    EnableMailbox,
    DisableMailbox,
    SetMailboxProperties,
    ManageDistribution,

    // Group Policy
    ManageGpoLinks,
}
