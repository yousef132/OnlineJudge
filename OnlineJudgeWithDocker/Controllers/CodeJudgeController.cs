using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Mvc;

namespace OnlineJudgeWithDocker.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CodeJudgeController : ControllerBase
    {
        private readonly DockerClient _dockerClient;
        private string _requestDirectory = null;

        public CodeJudgeController()
        {
            //  connect to Docker daemon
            _dockerClient = new DockerClientConfiguration(new Uri("tcp://localhost:2375")).CreateClient();
            _requestDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_requestDirectory);

        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitCode([FromBody] CodeSubmission submission)
        {


            string codeFilePath = await CreateMainFile(submission.Code, submission.Language);

            await CreateTestCasesFile(submission.TestCases);

            await CreateOutputAndErrorsFile();

            try
            {
                var output = await RunCodeInDocker(submission.Language, codeFilePath);
                return Ok(new { output });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            finally
            {
                // remove the directory with his files
                if (Directory.Exists(_requestDirectory))
                {
                    Directory.Delete(_requestDirectory, true);
                }

            }
        }
        #region lastest correct 

        //private async Task<Result> RunCodeInDocker(string language, string codeFilePath)
        //{
        //    string containerId = null;

        //    // Create testcases file 



        //    // Choose appropriate Docker image based on language
        //    var image = language switch
        //    {
        //        "python" => "python:3.8-slim",
        //        "cpp" => "gcc:latest",
        //        "csharp" => "mcr.microsoft.com/dotnet/sdk:5.0",
        //        _ => throw new ArgumentException("Unsupported language")
        //    };

        //    // string codeDirectory = Path.GetDirectoryName(codeFilePath);

        //    // Create a Docker container for the selected language
        //    var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
        //    {
        //        HostConfig = new HostConfig
        //        {
        //            Binds = new[]
        //            {
        //                 $"{_requestDirectory}:/code", // Mount the code and testcases to the same directory inside the container
        //            },
        //            NetworkMode = "none",  // Isolate the network for security
        //            Memory = 256 * 1024 * 1024, // Memory limit
        //            AutoRemove = false // Automatically remove the container when it exits
        //        },
        //        Image = image,
        //        // run the testcases against the code and capture the output in output.txt and the error in errors.txt 
        //        // these files are mounted to the copy in my host will be affected 
        //        // every time we run the program it we logs the errors or the output in these file then get the result 
        //        //then delete the files content 
        //        Cmd = language switch
        //        {
        //            "python" => new[] { "bash", "-c", "python3 /code/main.py < /code/testcases.txt > /code/output.txt 2> /code/error.txt" },
        //            "cpp" => new[] { "bash", "-c", "g++ /code/main.cpp -o /code/main 2> /code/error.txt && /code/main < /code/testcases.txt > /code/output.txt 2>> /code/error.txt" },
        //            "csharp" => new[] { "bash", "-c", "dotnet build /code/main.csproj -o /code/build 2> /code/error.txt && dotnet /code/build/main.dll < /code/testcases.txt > /code/output.txt 2>> /code/error.txt" },
        //            _ => throw new ArgumentException("Unsupported language")
        //        },
        //    });

        //    containerId = createContainerResponse.ID;

        //    // Start the container
        //    await _dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());
        //    // Wait for the container to finish executing

        //    var containerWaitResponse = await _dockerClient.Containers.WaitContainerAsync(containerId);

        //    Result result = new();
        //    // Read the output from the output.txt file 
        //    string outputPath = Path.Combine(_requestDirectory, "output.txt");
        //    result.Output = await System.IO.File.ReadAllTextAsync(outputPath);

        //    // Read the errors from the error.txt file
        //    string errorPath = Path.Combine(_requestDirectory, "error.txt");
        //    result.Errors = await System.IO.File.ReadAllTextAsync(errorPath);



        //    // Combine output and errors for final result
        //    return result;
        //} 
        #endregion

        private async Task<Result> RunCodeInDocker(string language, string codeFilePath)
        {
            string containerId = null;
            int timeLimitInSeconds = 5;

            // Set up a cancellation token for time limit
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeLimitInSeconds));

            // Choose the appropriate Docker image based on the language
            var image = language switch
            {
                "python" => "python:3.8-slim",
                "cpp" => "gcc:latest",
                "csharp" => "mcr.microsoft.com/dotnet/sdk:5.0",
                _ => throw new ArgumentException("Unsupported language")
            };

            // Create the Docker container for the selected language
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                HostConfig = new HostConfig
                {
                    Binds = new[]
                    {
                $"{_requestDirectory}:/code", // Mount the code and testcases to container
            },
                    NetworkMode = "none",
                    Memory = 256 * 1024 * 1024, // Set memory limit (256MB)
                    AutoRemove = true
                },
                Image = image,
                Cmd = language switch
                {
                    "python" => new[] { "bash", "-c", "python3 /code/main.py < /code/testcases.txt > /code/output.txt 2> /code/error.txt" },
                    "cpp" => new[] { "bash", "-c", "g++ /code/main.cpp -o /code/main 2> /code/error.txt && /code/main < /code/testcases.txt > /code/output.txt 2>> /code/error.txt" },
                    "csharp" => new[] { "bash", "-c", "dotnet build /code/main.csproj -o /code/build 2> /code/error.txt && dotnet /code/build/main.dll < /code/testcases.txt > /code/output.txt 2>> /code/error.txt" },
                    _ => throw new ArgumentException("Unsupported language")
                }
            });

            containerId = createContainerResponse.ID;

            try
            {
                // Start the container
                await _dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());

                // Run a task to stop the container if it exceeds the time limit
                var stopTask = Task.Delay(TimeSpan.FromSeconds(timeLimitInSeconds), cts.Token).ContinueWith(async (t) =>
                {
                    if (!t.IsCanceled)
                    {
                        await _dockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters());
                    }
                });

                // Wait for the container to finish or get stopped
                var waitTask = _dockerClient.Containers.WaitContainerAsync(containerId);

                // Await both wait and stop tasks
                await Task.WhenAny(waitTask, stopTask);

                if (stopTask.IsCompleted && !waitTask.IsCompleted)
                {
                    throw new TimeoutException($"Container exceeded the allowed time of {timeLimitInSeconds} seconds.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            // Read the output from output.txt
            Result result = new();
            string outputPath = Path.Combine(_requestDirectory, "output.txt");
            result.Output = await System.IO.File.ReadAllTextAsync(outputPath);

            // Read the errors from error.txt
            string errorPath = Path.Combine(_requestDirectory, "error.txt");
            result.Errors = await System.IO.File.ReadAllTextAsync(errorPath);

            return result;
        }



        private async Task<string> CreateMainFile(string content, string language)
        {

            var extension = language switch
            {
                "python" => "py",
                "cpp" => "cpp",
                "csharp" => "cs"
            };
            string fileName = $"main.{extension}";

            string codeFilePath = Path.Combine(_requestDirectory, fileName);

            // Save code to a temp file with the fixed name
            await System.IO.File.WriteAllTextAsync(codeFilePath, content);

            return codeFilePath;
        }
        private async Task<string> CreateTestCasesFile(List<string> testcases)
        {
            string testCasesPath = Path.Combine(_requestDirectory, "testcases.txt");
            await System.IO.File.WriteAllLinesAsync(testCasesPath, testcases);

            return testCasesPath;

        }
        private async Task CreateOutputAndErrorsFile()
        {
            string outputPath = Path.Combine(_requestDirectory, "output.txt");
            string errorPath = Path.Combine(_requestDirectory, "error.txt");

            await System.IO.File.WriteAllTextAsync(outputPath, string.Empty); // Clear or create output.txt
            await System.IO.File.WriteAllTextAsync(errorPath, string.Empty);

        }


    }

    public class CodeSubmission
    {
        public string Code { get; set; }
        public string Language { get; set; } // e.g. "cpp", "python", "csharp"
        public List<string> TestCases { get; set; }
    }
    public class Result
    {
        public string Output { get; set; }
        public string Errors { get; set; } = "No Errors";
    }

}
