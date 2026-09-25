using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text.Json.Serialization;

namespace AGLR_Launcher
{
    public class AdminService : IDisposable
    {
        private readonly IMongoCollection<AdminUser> _users;
        private readonly MongoClient _client;

        public AdminService(string username, string password)
        {
            string uri = $"mongodb+srv://{username}:{password}@vulu.wjhda8r.mongodb.net/launcher?appName=vulu";
            var settings = MongoClientSettings.FromConnectionString(uri);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

            _client = new MongoClient(settings);
            var db = _client.GetDatabase("launcher");
            _users = db.GetCollection<AdminUser>("users");
        }

        /// <summary>
        /// Bağlantıyı test eder. Başarısızsa exception fırlatır.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            await _client.GetDatabase("launcher")
                .RunCommandAsync((Command<BsonDocument>)"{ping:1}");
        }

        public async Task<List<AdminUser>> GetUsersAsync()
        {
            return await _users.Find(_ => true).ToListAsync();
        }

        public async Task AddUserAsync(AdminUser user)
        {
            await _users.InsertOneAsync(user);
        }

        public async Task UpdateUserAsync(string id, UpdateDefinition<AdminUser> update)
        {
            await _users.UpdateOneAsync(
                Builders<AdminUser>.Filter.Eq(u => u.Id, id), update);
        }

        public async Task DeleteUserAsync(string id)
        {
            await _users.DeleteOneAsync(
                Builders<AdminUser>.Filter.Eq(u => u.Id, id));
        }

        public void Dispose()
        {
            // MongoClient bağlantı havuzunu yönetir, kapatmaya gerek yok
            // ama interface'i implement ediyoruz, gelecekte lazım olabilir
        }

    }

    [BsonIgnoreExtraElements]
    public class AdminUser
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("username")]
        public string Username { get; set; } = string.Empty;

        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("allowedPacks")]
        public List<string> AllowedPacks { get; set; } = new();

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;

        [BsonElement("isAdmin")]
        public bool IsAdmin { get; set; } = false;
    }
}