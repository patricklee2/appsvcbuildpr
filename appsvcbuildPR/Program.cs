﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LibGit2Sharp;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp.Handlers;
using System.Security.AccessControl;

namespace appsvcbuildPR
{
    class Program
    {
        static void Main(string[] args)
        {
            //String stack = "node";
            //createPR(stack);
            createPR("dotnetcore");
            createPR("node");
            createPR("php");
            createPR("python");
            createPR("ruby");

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

        private static Boolean isDotnetcoreRepo(Repo r)
        {
            return isStackRepo(r, "dotnetcore");
        }

        private static Boolean isNodeRepo(Repo r)
        {
            return isStackRepo(r, "node");
        }

        private static Boolean isPhpRepo(Repo r)
        {
            return isStackRepo(r, "php");
        }

        private static Boolean isPythonRepo(Repo r)
        {
            return isStackRepo(r, "python");
        }

        private static Boolean isRubyRepo(Repo r)
        {
            return r.name.ToLower().Contains("ruby") && !r.name.ToLower().Contains("base");
        }

        private static Boolean isRubyBaseRepo(Repo r)
        {
            return r.name.ToLower().Contains("rubybase");
        }

        private static Boolean isStackRepo(Repo r, String stack)
        {
            return r.name.ToLower().Contains(stack) && !r.name.ToLower().Contains("app");
        }

        public static void rubyBase(List<Repo> repos, String root, String upstream)
        {
            Repository repo = new Repository(upstream);
            foreach (Repo r in repos)
            {
                // pull temps
                String dest = root + "\\" + r.name;
                Repository.Clone(r.clone_url, dest, new CloneOptions { BranchName = "master" });

                // move
                String version = r.name.ToLower().Replace("rubybase-", "");
                DeepCopy(dest, upstream + "\\base_images\\" + version);

                // stage
                Commands.Stage(repo, upstream + "\\base_images\\" + version);
            }
        }

        public static async void createPR(String stack)
        {
            String _gitToken = ""; //fill me in
            // clone master
            String timeStamp = DateTime.Now.ToString("yyyyMMddHHmm");
            String root = String.Format("D:\\home\\site\\wwwroot\\appsvcbuildPR{0}", timeStamp);
            String upstream = root + "\\" + stack;
            String upstreamURL = String.Format("https://github.com/Azure-App-Service/{0}.git", stack);
            String branch = String.Format("appsvcbuild{0}", timeStamp);
            Repository.Clone(upstreamURL, upstream, new CloneOptions { BranchName = "dev" });

            // branch
            Repository repo = new Repository(upstream);
            repo.CreateBranch(branch);
            Commands.Checkout(repo, branch);

            // list temp repos
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("patricklee2");
            String generatedReposURL = String.Format("https://api.github.com/orgs/{0}/repos", "blessedimagepipeline");
            HttpResponseMessage response = await httpClient.GetAsync(generatedReposURL);
            response.EnsureSuccessStatusCode();
            string contentString = await response.Content.ReadAsStringAsync();
            List<Repo> resultList = JsonConvert.DeserializeObject<List<Repo>>(contentString);
            List<Repo> stackRepos = null;

            switch (stack)
            {
                case "dotnetcore":
                    stackRepos = resultList.FindAll(isDotnetcoreRepo);
                    break;
                case "node":
                    stackRepos = resultList.FindAll(isNodeRepo);
                    break;
                case "php":
                    stackRepos = resultList.FindAll(isPhpRepo);
                    break;
                case "python":
                    stackRepos = resultList.FindAll(isPythonRepo);
                    break;
                case "ruby":
                    stackRepos = resultList.FindAll(isRubyRepo);
                    rubyBase(resultList.FindAll(isRubyBaseRepo), root, upstream);
                    break;
            }
            // List<Repo> stackRepos = resultList.FindAll(isStackRepo(stack));

            foreach (Repo r in stackRepos)
            {
                // pull temps
                String dest = root + "\\" + r.name;
                Repository.Clone(r.clone_url, dest, new CloneOptions { BranchName = "master" });

                // move
                String version = r.name.ToLower().Replace(stack +"-", "");
                DeepCopy(dest, upstream + "\\" + version);

                // stage
                Commands.Stage(repo, upstream + "\\" + version);
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
            repo.Network.Push(repo.Branches[branch], options); // fails if branch already exists
            
            //create PR
            
            String pullRequestURL = String.Format("https://api.github.com/repos/{0}/{1}/pulls?access_token={2}", "azure-app-service", stack, _gitToken);
            String body =
                "{ " +
                    "\"title\": " + JsonConvert.SerializeObject("sync from templates") + ", " +
                    "\"body\": " + JsonConvert.SerializeObject("sync from templates") + ", " +
                    "\"head\": " + JsonConvert.SerializeObject("azure-app-service:" + branch) + ", " +
                    "\"base\": " + JsonConvert.SerializeObject("dev") +
                    
                "}";

            response = await httpClient.PostAsync(pullRequestURL, new StringContent(body)); // fails on empty commits
            
            String result = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine(response.ToString());
            System.Console.WriteLine(result);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                System.Console.WriteLine("Unable to make PR due to no differnce");
            }

            //cleanup
            //new DirectoryInfo(root).Delete(true);
        }
    }
}
