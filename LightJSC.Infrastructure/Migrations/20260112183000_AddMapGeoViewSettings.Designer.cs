using LightJSC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    [DbContext(typeof(IngestorDbContext))]
    [Migration("20260112183000_AddMapGeoViewSettings")]
    partial class AddMapGeoViewSettings
    {
    }
}
