using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Entities;

public partial class FlexifitContext : DbContext
{
    public FlexifitContext()
    {
    }

    public FlexifitContext(DbContextOptions<FlexifitContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
