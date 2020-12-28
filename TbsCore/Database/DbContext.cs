﻿using Microsoft.EntityFrameworkCore;
using TravBotSharp.Files.Helpers;

namespace TbsCore.Database
{
    public class TbsContext : DbContext
    {
        public TbsContext()
        {
            Database.EnsureCreated();
        }
        public TbsContext(DbContextOptions<TbsContext> options) : base(options) { }

        public DbSet<DbAccount> DbAccount { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={@IoHelperCore.SqlitePath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DbAccount>().ToTable("DbAccount");
        }
    }
}
