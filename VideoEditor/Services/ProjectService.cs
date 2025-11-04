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
                // 이 설정 덕분에 VideoClip, AudioClip 등 자식 클래스 타입을 구별하여 저장/복원할 수 있습니다.
                TypeNameHandling = TypeNameHandling.Auto,
                // JSON 파일을 사람이 읽기 쉽게 들여쓰기 형식으로 만듭니다.
                Formatting = Formatting.Indented
            };
        }

        /// <summary>
        /// 프로젝트 데이터를 지정된 경로의 파일에 JSON 형식으로 저장합니다.
        /// </summary>
        /// <param name="projectData">저장할 모든 데이터가 담긴 객체</param>
        /// <param name="filePath">저장할 파일의 전체 경로</param>
        public async Task SaveProjectAsync(ProjectSaveData projectData, string filePath)
        {
            string json = JsonConvert.SerializeObject(projectData, _serializerSettings);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// 지정된 경로의 프로젝트 파일에서 데이터를 읽어와 객체로 복원합니다.
        /// </summary>
        /// <param name="filePath">불러올 파일의 전체 경로</param>
        /// <returns>복원된 프로젝트 데이터 객체</returns>
        public async Task<ProjectSaveData?> LoadProjectAsync(string filePath)
        {
            string json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<ProjectSaveData>(json, _serializerSettings);
        }
    }
}