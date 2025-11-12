using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VideoEditor.Models;

namespace VideoEditor.Services
{
    public class ProjectService
    {
        private readonly JsonSerializerSettings _serializerSettings;

        public ProjectService()
        {
            _serializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented
            };
        }

        public async Task SaveProjectAsync(ProjectSaveData projectData, string filePath)
        {
            string json = JsonConvert.SerializeObject(projectData, _serializerSettings);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<ProjectSaveData?> LoadProjectAsync(string filePath)
        {
            string json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<ProjectSaveData>(json, _serializerSettings);
        }
    }
}