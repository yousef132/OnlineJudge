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


        //private async Task<Result> RunCodeInDocker(string language, string codeFilePath)
        //{
        //    string containerId = null;
        //    int timeLimitInSeconds = 5;

        //    // Set up a cancellation token for the time limit
        //    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeLimitInSeconds));

        //    // Choose the appropriate Docker image based on the language
        //    var image = language switch
        //    {
        //        "python" => "python:3.8-slim",
        //        "cpp" => "gcc:latest",
        //        "csharp" => "mcr.microsoft.com/dotnet/sdk:5.0",
        //        _ => throw new ArgumentException("Unsupported language")
        //    };

        //    // Create the Docker container for the selected language
        //    var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
        //    {
        //        HostConfig = new HostConfig
        //        {
        //            Binds = new[]
        //            {
        //              $"{_requestDirectory}:/code", // Mount the code and test cases to container
        //             },
        //            NetworkMode = "none",  // Isolate the network for security
        //            Memory = 256 * 1024 * 1024, // Set memory limit (256MB)
        //            AutoRemove = true
        //        },
        //        Image = image,

        //        Cmd = language switch
        //        {
        //            // The time command measures real execution time and logs output/errors
        //            "python" => new[] { "bash", "-c", " { time python3 /code/main.py < /code/testcases.txt > /code/output.txt; } 2> /code/runtime.txt" },
        //            "cpp" => new[] { "bash", "-c", " { g++ /code/main.cpp -o /code/main 2> /code/error.txt && time /code/main < /code/testcases.txt > /code/output.txt; } 2> /code/runtime.txt" },
        //            "csharp" => new[] { "bash", "-c", " { dotnet build /code/main.csproj -o /code/build 2> /code/error.txt && time dotnet /code/build/main.dll < /code/testcases.txt > /code/output.txt; } 2> /code/runtime.txt" },
        //            _ => throw new ArgumentException("Unsupported language")
        //        }
        //    });

        //    containerId = createContainerResponse.ID;


        //    try
        //    {
        //        // Start the container
        //        await _dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());

        //        // Run a task to stop the container if it exceeds the time limit
        //        var stopTask = Task.Delay(TimeSpan.FromSeconds(timeLimitInSeconds), cts.Token).ContinueWith(async (t) =>
        //        {
        //            if (!t.IsCanceled)
        //            {
        //                await _dockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters());
        //            }
        //        });

        //        // Wait for the container to finish or get stopped
        //        var waitTask = _dockerClient.Containers.WaitContainerAsync(containerId);

        //        // Await both wait and stop tasks
        //        await Task.WhenAny(waitTask, stopTask);

        //        if (stopTask.IsCompleted && !waitTask.IsCompleted)
        //        {
        //            throw new TimeoutException($"Container exceeded the allowed time of {timeLimitInSeconds} seconds.");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error: {ex.Message}");
        //    }

        //    // Read the output, errors, and runtime from files
        //    Result result = new();
        //    string outputPath = Path.Combine(_requestDirectory, "output.txt");
        //    result.Output = await System.IO.File.ReadAllTextAsync(outputPath);

        //    string errorPath = Path.Combine(_requestDirectory, "error.txt");
        //    result.Errors = await System.IO.File.ReadAllTextAsync(errorPath);

        //    string runtimePath = Path.Combine(_requestDirectory, "runtime.txt");
        //    result.Runtime = await System.IO.File.ReadAllTextAsync(runtimePath);

        //    return result;
        //}




        private async Task<Result> RunCodeInDocker(string language, string codeFilePath)
        {
            string containerId = null;

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
                          $"{_requestDirectory}:/code", // Mount the code and test cases to container
                    },
                    NetworkMode = "none",  // Isolate the network for security
                    Memory = 256 * 1024 * 1024, // Set memory limit (256MB)
                    AutoRemove = true
                },
                Image = image,

                Cmd = language switch
                {
                    // The time command measures real execution time and terminates after 5 seconds
                    "python" => new[] {
                          "bash",
                          "-c",
                          "timeout 5s bash -c '{ time python3 /code/main.py < /code/testcases.txt > /code/output.txt; }' 2> /code/runtime.txt; if [ $? -eq 124 ]; then echo 'Process terminated due to timeout' >> /code/runtime.txt; fi"
                    },

                    "cpp" => new[] {
                     "bash",
                     "-c",
                     "timeout 5s bash -c '{ g++ /code/main.cpp -o /code/main 2> /code/error.txt && time /code/main < /code/testcases.txt > /code/output.txt; }' 2> /code/runtime.txt; if [ $? -eq 124 ]; then echo 'Process terminated due to timeout' >> /code/runtime.txt; fi"
                    },

                    "csharp" => new[] {
                       "bash",
                       "-c",
                       "timeout 5s bash -c '{ dotnet build /code/main.csproj -o /code/build 2> /code/error.txt && time dotnet /code/build/main.dll < /code/testcases.txt > /code/output.txt; }' 2> /code/runtime.txt; if [ $? -eq 124 ]; then echo 'Process terminated due to timeout' >> /code/runtime.txt; fi"
                    },

                    _ => throw new ArgumentException("Unsupported language")
                }
            });

            containerId = createContainerResponse.ID;

            try
            {
                // Start the container
                await _dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());

                // Wait for the container to finish or be stopped
                await _dockerClient.Containers.WaitContainerAsync(containerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            var containerInfo = await _dockerClient.Containers.InspectContainerAsync(containerId);
            var exitCode = containerInfo.State.ExitCode;

            // Read the output, errors, and runtime from files
            Result result = new();
            string outputPath = Path.Combine(_requestDirectory, "output.txt");
            result.Output = await System.IO.File.ReadAllTextAsync(outputPath);

            string errorPath = Path.Combine(_requestDirectory, "error.txt");
            result.Errors = await System.IO.File.ReadAllTextAsync(errorPath);

            string runtimePath = Path.Combine(_requestDirectory, "runtime.txt");
            result.Runtime = await System.IO.File.ReadAllTextAsync(runtimePath);

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

            await System.IO.File.WriteAllTextAsync(codeFilePath, content);

            #region addition file to run c# code

            if (language == "csharp")
            {
                string csprojFile = Path.Combine(_requestDirectory, "main.csproj");
                string ProjectConfigs = "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\r\n  <PropertyGroup>\r\n    <OutputType>Exe</OutputType>\r\n    <TargetFramework>net5.0</TargetFramework>\r\n    <ImplicitUsings>enable</ImplicitUsings>\r\n    <Nullable>enable</Nullable>\r\n  </PropertyGroup>\r\n\r\n</Project>\r\n";

                // Save code to a temp file with the fixed name
                await System.IO.File.WriteAllTextAsync(csprojFile, ProjectConfigs);

            }
            #endregion

            return codeFilePath;
        }
        private async Task<string> CreateTestCasesFile(string testcases)
        {
            string testCasesPath = Path.Combine(_requestDirectory, "testcases.txt");
            await System.IO.File.WriteAllTextAsync(testCasesPath, testcases);
            return testCasesPath;

        }
        private async Task CreateOutputAndErrorsFile()
        {
            string outputPath = Path.Combine(_requestDirectory, "output.txt");
            string errorPath = Path.Combine(_requestDirectory, "error.txt");
            string runTimePath = Path.Combine(_requestDirectory, "runtime.txt");

            await System.IO.File.WriteAllTextAsync(outputPath, string.Empty);
            await System.IO.File.WriteAllTextAsync(errorPath, string.Empty);
            await System.IO.File.WriteAllTextAsync(runTimePath, string.Empty);
        }

    }

    public class CodeSubmission
    {
        public string Code { get; set; }
        public string Language { get; set; } // e.g. "cpp", "python", "csharp"
        public string TestCases { get; set; }
    }
    public class Result
    {
        public string Output { get; set; }
        public string Errors { get; set; }
        public string Runtime { get; set; }
    }

}
