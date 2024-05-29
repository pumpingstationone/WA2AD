using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncDB
{
    public class MemberDB
    {
        private SQLiteConnection sqlite_conn;


        // This is the class that will handle all the database work of
        // maintaining a local sqlite database of Wild Apricot members'
        // names, IDs, and the last time they were synced via the WA API

        // First thing we need to do is check to see if the database file
        // exists and if not, create it and the table we need
        public MemberDB()
        {
            // Check to see if the database file exists
            if (!System.IO.File.Exists("WA2AD.db"))
            {
                // If it doesn't, create it and the table we need
                using (System.Data.SQLite.SQLiteConnection conn = new System.Data.SQLite.SQLiteConnection("Data Source=WA2AD.db;Version=3;"))
                {
                    conn.Open();
                    using (System.Data.SQLite.SQLiteCommand cmd = new System.Data.SQLite.SQLiteCommand("CREATE TABLE Members (id INTEGER PRIMARY KEY, wa_id INTEGER, first_name TEXT, last_name TEXT, last_sync TEXT)", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    // We also need another table to indicate the last time we synced the data
                    using (System.Data.SQLite.SQLiteCommand cmd = new System.Data.SQLite.SQLiteCommand("CREATE TABLE Sync (last_sync TEXT)", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            
            // And now we can open the connection to the database
            this.sqlite_conn = new SQLiteConnection("Data Source=WA2AD.db;Version=3;");
            this.sqlite_conn.Open();
        }

        // This method will add a member to the database
        public void AddMember(int wa_id, string first_name, string last_name)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(this.sqlite_conn))
            {
                cmd.CommandText = "INSERT INTO Members (wa_id, first_name, last_name, last_sync) VALUES (@wa_id, @first_name, @last_name, @last_sync)";
                cmd.Parameters.AddWithValue("@wa_id", wa_id);
                cmd.Parameters.AddWithValue("@first_name", first_name);
                cmd.Parameters.AddWithValue("@last_name", last_name);
                cmd.Parameters.AddWithValue("@last_sync", DateTime.Now.ToString());
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateMember(int wa_id, string first_name, string last_name)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(this.sqlite_conn))
            {
                // First we need to see if the member is already in the database
                // (yes I know upsert is a thing, but I've had weirdness using it
                // with sqlite so I'm just going to do it the long way)
                cmd.CommandText = "SELECT COUNT(*) FROM Members WHERE wa_id = @wa_id";
                cmd.Parameters.AddWithValue("@wa_id", wa_id);
                int count = Convert.ToInt32(cmd.ExecuteScalar());

                // If the member is not in the database, add them
                if (count == 0)
                {
                    AddMember(wa_id, first_name, last_name);
                    return;
                }

                cmd.CommandText = "UPDATE Members SET first_name = @first_name, last_name = @last_name, last_sync = @last_sync WHERE wa_id = @wa_id";
                cmd.Parameters.AddWithValue("@wa_id", wa_id);
                cmd.Parameters.AddWithValue("@first_name", first_name);
                cmd.Parameters.AddWithValue("@last_name", last_name);
                cmd.Parameters.AddWithValue("@last_sync", DateTime.Now.ToString());
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateSyncTime()
        {
            using (SQLiteCommand cmd = new SQLiteCommand(this.sqlite_conn))
            {
                cmd.CommandText = "insert or replace into Sync (last_sync) values (@last_sync)";
                cmd.Parameters.AddWithValue("@last_sync", DateTime.Now.ToString());
                cmd.ExecuteNonQuery();
            }
        }

        public string GetLastSyncTime()
        {
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT last_sync FROM Sync", this.sqlite_conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        // We need to return the value in YYY-MM-DD format
                        return DateTime.Parse(reader[0].ToString()).ToString("yyyy-MM-dd");
                    }
                }
            }

            // If we didn't find a last sync time, return the current date
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        public void Close()
        {
            this.sqlite_conn.Close();
        }
    }
}
