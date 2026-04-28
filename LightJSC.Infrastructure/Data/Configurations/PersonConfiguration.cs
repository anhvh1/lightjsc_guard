using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("persons");
        builder.HasKey(person => person.Id);

        builder.Property(person => person.Code).HasMaxLength(100);
        builder.Property(person => person.FirstName).HasMaxLength(200);
        builder.Property(person => person.LastName).HasMaxLength(200);
        builder.Property(person => person.PersonalId).HasMaxLength(32);
        builder.Property(person => person.DocumentNumber).HasMaxLength(32);
        builder.Property(person => person.Address).HasMaxLength(500);
        builder.Property(person => person.RawQrPayload).HasMaxLength(4000);
        builder.Property(person => person.Gender).HasMaxLength(50);
        builder.Property(person => person.Remarks).HasMaxLength(500);
        builder.Property(person => person.Category).HasMaxLength(100);
        builder.Property(person => person.ListType).HasMaxLength(32);

        builder.HasIndex(person => person.Code).IsUnique();
        builder.HasIndex(person => person.PersonalId);
        builder.HasIndex(person => person.DocumentNumber);
        builder.HasIndex(person => person.IsActive);
        builder.HasIndex(person => person.UpdatedAt);
    }
}

