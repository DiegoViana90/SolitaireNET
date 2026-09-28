using Microsoft.Data.Sqlite;

namespace SolitaireNET.WebApi.Agenda;

public sealed record AgendaService(int Id, string Name, int DurationMinutes, decimal Price, bool Active);
public sealed record AgendaBooking(long Id, int ServiceId, string ServiceName, string CustomerName, string CustomerPhone, string StartsAt, string Status);

public sealed class AgendaStore
{
    private readonly string _connectionString;

    public AgendaStore(IConfiguration configuration)
    {
        string path = configuration["Agenda:DatabasePath"] ?? "/opt/solitairenet-api/data/agenda.db";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS agenda_services (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                duration_minutes INTEGER NOT NULL,
                price_cents INTEGER NOT NULL,
                active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS agenda_bookings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                service_id INTEGER NOT NULL,
                customer_name TEXT NOT NULL,
                customer_phone TEXT NOT NULL,
                starts_at TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'booked',
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }

    public IReadOnlyList<AgendaService> Services(bool includeInactive = false)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,duration_minutes,price_cents,active FROM agenda_services " + (includeInactive ? "" : "WHERE active=1 ") + "ORDER BY name";
        using var r = cmd.ExecuteReader(); var result = new List<AgendaService>();
        while (r.Read()) result.Add(new(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3) / 100m, r.GetInt32(4) == 1));
        return result;
    }

    public AgendaService AddService(string name, int durationMinutes, decimal price)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO agenda_services(name,duration_minutes,price_cents) VALUES ($name,$duration,$price); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$name", name.Trim()); cmd.Parameters.AddWithValue("$duration", durationMinutes); cmd.Parameters.AddWithValue("$price", (long)Math.Round(price * 100));
        long id = (long)cmd.ExecuteScalar()!; return new((int)id, name.Trim(), durationMinutes, price, true);
    }

    public bool IsAvailable(int serviceId, string startsAt)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agenda_bookings WHERE service_id=$service AND starts_at=$starts AND status='booked'";
        cmd.Parameters.AddWithValue("$service", serviceId); cmd.Parameters.AddWithValue("$starts", startsAt);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 0;
    }

    public AgendaBooking? Book(int serviceId, string customerName, string customerPhone, string startsAt)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var check = c.CreateCommand(); check.Transaction = tx; check.CommandText = "SELECT COUNT(*) FROM agenda_bookings WHERE service_id=$service AND starts_at=$starts AND status='booked'";
        check.Parameters.AddWithValue("$service", serviceId); check.Parameters.AddWithValue("$starts", startsAt);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return null;
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO agenda_bookings(service_id,customer_name,customer_phone,starts_at,created_at) VALUES($service,$name,$phone,$starts,$created); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$service", serviceId); cmd.Parameters.AddWithValue("$name", customerName.Trim()); cmd.Parameters.AddWithValue("$phone", customerPhone.Trim()); cmd.Parameters.AddWithValue("$starts", startsAt); cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        long id = (long)cmd.ExecuteScalar()!; tx.Commit();
        var service = Services().FirstOrDefault(item => item.Id == serviceId);
        return service == null ? null : new(id, serviceId, service.Name, customerName.Trim(), customerPhone.Trim(), startsAt, "booked");
    }

    public IReadOnlyList<AgendaBooking> Bookings(string? from = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT b.id,b.service_id,s.name,b.customer_name,b.customer_phone,b.starts_at,b.status FROM agenda_bookings b JOIN agenda_services s ON s.id=b.service_id WHERE ($from IS NULL OR b.starts_at >= $from) ORDER BY b.starts_at"; cmd.Parameters.AddWithValue("$from", (object?)from ?? DBNull.Value);
        using var r = cmd.ExecuteReader(); var result = new List<AgendaBooking>(); while (r.Read()) result.Add(new(r.GetInt64(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6))); return result;
    }
}
