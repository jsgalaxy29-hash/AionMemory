using Aion.Domain;

namespace Aion.Domain.SeedData;

public static class ContactsModuleDefinition
{
    public static readonly Guid ModuleId = Guid.Parse("f4e8c7e2-bad5-4c96-9c0c-7f4bcb8f3411");
    public static readonly Guid EntityId = Guid.Parse("9df0d7c8-65c9-4c1a-9ef3-b6d998b3b672");
    public static readonly Guid FirstNameFieldId = Guid.Parse("c3af0ad5-2cfa-4aa4-9c31-3be06f1e8a3d");
    public static readonly Guid LastNameFieldId = Guid.Parse("1bb5e7f1-26df-4c20-8f5b-1b5238a82097");
    public static readonly Guid EmailFieldId = Guid.Parse("6e59a5b9-097a-449d-9d76-9f5f91f6f2b4");
    public static readonly Guid PhoneFieldId = Guid.Parse("6cfd1d0f-52c6-4a39-97d7-4b7d69b5c1ea");
    public static readonly Guid NotesFieldId = Guid.Parse("7b1a8f57-6cf1-4efd-8b1d-6d0132d072c5");
    public static readonly Guid EmailViewId = Guid.Parse("7bc2bc75-08f4-4fe1-9857-3c6fb0502ae0");

    public static S_Module CreateModule()
    {
        return new S_Module
        {
            Id = ModuleId,
            Name = "Contacts",
            Description = "Carnet d'adresses connecté au DataEngine",
            EntityTypes = { CreateEntityType() }
        };
    }

    public static S_EntityType CreateEntityType()
    {
        return new S_EntityType
        {
            Id = EntityId,
            ModuleId = ModuleId,
            Name = "Contact",
            PluralName = "Contacts",
            Description = "Carnet d'adresses minimal pour la démo",
            Fields =
            {
                new S_Field
                {
                    Id = FirstNameFieldId,
                    EntityTypeId = EntityId,
                    Name = "firstName",
                    Label = "Prénom",
                    DataType = FieldDataType.Text,
                    IsRequired = true,
                    IsSearchable = true,
                    IsListVisible = true
                },
                new S_Field
                {
                    Id = LastNameFieldId,
                    EntityTypeId = EntityId,
                    Name = "lastName",
                    Label = "Nom",
                    DataType = FieldDataType.Text,
                    IsRequired = true,
                    IsSearchable = true,
                    IsListVisible = true
                },
                new S_Field
                {
                    Id = EmailFieldId,
                    EntityTypeId = EntityId,
                    Name = "email",
                    Label = "Email",
                    DataType = FieldDataType.Text,
                    IsSearchable = true,
                    IsListVisible = true
                },
                new S_Field
                {
                    Id = PhoneFieldId,
                    EntityTypeId = EntityId,
                    Name = "phone",
                    Label = "Téléphone",
                    DataType = FieldDataType.Text,
                    IsSearchable = true,
                    IsListVisible = true
                },
                new S_Field
                {
                    Id = NotesFieldId,
                    EntityTypeId = EntityId,
                    Name = "notes",
                    Label = "Notes",
                    DataType = FieldDataType.Text
                }
            }
        };
    }

    public static STable CreateTable()
    {
        return new STable
        {
            Id = EntityId,
            Name = "Contact",
            DisplayName = "Contacts",
            Description = "Carnet d'adresses minimal pour la démo",
            Fields = new List<SFieldDefinition>
            {
                new()
                {
                    Id = FirstNameFieldId,
                    TableId = EntityId,
                    Name = "firstName",
                    Label = "Prénom",
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new()
                {
                    Id = LastNameFieldId,
                    TableId = EntityId,
                    Name = "lastName",
                    Label = "Nom",
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new()
                {
                    Id = EmailFieldId,
                    TableId = EntityId,
                    Name = "email",
                    Label = "Email",
                    DataType = FieldDataType.Text
                },
                new()
                {
                    Id = PhoneFieldId,
                    TableId = EntityId,
                    Name = "phone",
                    Label = "Téléphone",
                    DataType = FieldDataType.Text
                },
                new()
                {
                    Id = NotesFieldId,
                    TableId = EntityId,
                    Name = "notes",
                    Label = "Notes",
                    DataType = FieldDataType.Text
                }
            },
            Views = new List<SViewDefinition>
            {
                new()
                {
                    Id = EmailViewId,
                    TableId = EntityId,
                    Name = "Email uniquement",
                    QueryDefinition = "{ \"email\": \"\" }",
                    Visualization = "table"
                }
            }
        };
    }
}
