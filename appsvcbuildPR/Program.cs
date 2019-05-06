using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LibGit2Sharp;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp.Handlers;

namespace appsvcbuildPR
{
    class Program
    {
        static void Main(string[] args)
        {
            createPR();
            while (true)    //sleep until user exits
            {
                System.Threading.Thread.Sleep(5000);
            }
        }

        public static void DeepCopy(String source, String target)
        {
            DeepCopy(new DirectoryInfo(source), new DirectoryInfo(target));
        }

        public static void DeepCopy(DirectoryInfo source, DirectoryInfo target)
        {
            target.Create();
            // Recursively call the DeepCopy Method for each Directory
            foreach (DirectoryInfo dir in source.GetDirectories())
                DeepCopy(dir, target.CreateSubdirectory(dir.Name));

            // Go ahead and copy each file in "source" to the "target" directory
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }

        private static Boolean isNodeRepo(Repo r)
        {
            return r.name.ToLower().Contains("node") && !r.name.ToLower().Contains("app");
        }

        public static async void createPR()
        {
            String _gitToken = ""; //fill me in
            // clone master
            String i = new Random().Next(0, 9999).ToString();
            String root = String.Format("D:\\home\\site\\wwwroot\\appsvcbuildPR{0}", i);
            String upstream = root + "\\node";
            String upstreamURL = "https://github.com/Azure-App-Service/node.git";
            Repository.Clone(upstreamURL, upstream, new CloneOptions { BranchName = "dev" });

            // branch
            Repository repo = new Repository(upstream);
            repo.CreateBranch("appsvcbuild");
            Commands.Checkout(repo, "appsvcbuild");

            // list temp repos
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("patricklee2");
            String generatedReposURL = String.Format("https://api.github.com/orgs/{0}/repos", "blessedimagepipeline");
            HttpResponseMessage response = await httpClient.GetAsync(generatedReposURL);
            response.EnsureSuccessStatusCode();
            string contentString = await response.Content.ReadAsStringAsync();
            List<Repo> resultList = JsonConvert.DeserializeObject<List<Repo>>(contentString);
            List<Repo> nodeRepos = resultList.FindAll(isNodeRepo);

            foreach (Repo r in nodeRepos)
            {
                // pull temps
                String dest = root + "\\" + r.name;
                Repository.Clone(r.clone_url, dest, new CloneOptions { BranchName = "master" });

                // move
                String version = r.name.ToLower().Replace("node-", "").Replace('-', '.');
                DeepCopy(dest, upstream + "//" + version);

                // stage
                Commands.Stage(repo, upstream + "//" + version);
            }

            
            // git commit
            // Create the committer's signature and commit
            //_log.Info("git commit");
            Signature author = new Signature("appsvcbuild", "patle@microsoft.com", DateTime.Now);
            Signature committer = author;

            // Commit to the repository
            try
            {
                Commit commit = repo.Commit("appsvcbuild", author, committer);
            }
            catch (Exception e)
            {
                //_log.info("Empty commit");
            }

            Remote remote = repo.Network.Remotes.Add("upstream", upstreamURL);
            repo.Branches.Update(repo.Head, b => b.Remote = remote.Name, b => b.UpstreamBranch = repo.Head.CanonicalName);

            // git push
            //_log.Info("git push");
            LibGit2Sharp.PushOptions options = new LibGit2Sharp.PushOptions();
            options.CredentialsProvider = new CredentialsHandler(
                (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials()
                    {
                        Username = _gitToken,
                        Password = String.Empty
                    });
            repo.Network.Push(repo.Branches["appsvcbuild"], options);
            
            //create PR
            
            String pullRequestURL = String.Format("https://api.github.com/repos/{0}/{1}/pulls?access_token={2}", "azure-app-service", "node", _gitToken);
            String body =
                "{ " +
                    "\"title\": " + JsonConvert.SerializeObject("test") + ", " +
                    "\"body\": " + JsonConvert.SerializeObject("test") + ", " +
                    "\"head\": " + JsonConvert.SerializeObject("azure-app-service:appsvcbuild") + ", " +
                    "\"base\": " + JsonConvert.SerializeObject("dev") +
                    
                "}";

            response = await httpClient.PostAsync(pullRequestURL, new StringContent(body)); // fails on empty commits
            String result = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine(response.ToString());
            System.Console.WriteLine(result);
        }
    }
}
