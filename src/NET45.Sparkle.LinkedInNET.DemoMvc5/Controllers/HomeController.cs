namespace Sparkle.LinkedInNET.DemoMvc5.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web.Mvc;

    using Newtonsoft.Json;

    using Sparkle.LinkedInNET.Asset;
    using Sparkle.LinkedInNET.Common;
    using Sparkle.LinkedInNET.DemoMvc5.Domain;
    using Sparkle.LinkedInNET.OAuth2;
    using Sparkle.LinkedInNET.Organizations;
    using Sparkle.LinkedInNET.Profiles;
    using Sparkle.LinkedInNET.Shares;
    using Sparkle.LinkedInNET.Targeting;
    using Sparkle.LinkedInNET.UGCPost;
    using Sparkle.LinkedInNET.Videos;

    ////using Sparkle.LinkedInNET.ServiceDefinition;

    public class HomeController : Controller
    {
        private LinkedInApi api;
        private DataService data;
        private LinkedInApiConfiguration apiConfig;
        private readonly List<string> errors = new List<string>();

        public HomeController(LinkedInApi api, DataService data, LinkedInApiConfiguration apiConfig)
        {
            this.api = api;
            this.data = data;
            this.apiConfig = apiConfig;        
        }

        public async Task<ActionResult> Index(string culture = "en-US")
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // step 1: configuration
            this.ViewBag.Configuration = this.apiConfig;
            
            // step 2: authorize url
            var scope = AuthorizationScope.ReadEmailAddress | AuthorizationScope.ReadWriteCompanyPage | AuthorizationScope.WriteShare;
            var state = Guid.NewGuid().ToString();
            var redirectUrl = this.Request.Compose() + this.Url.Action("OAuth2");
            this.ViewBag.LocalRedirectUrl = redirectUrl;
            if (this.apiConfig != null && !string.IsNullOrEmpty(this.apiConfig.ApiKey))
            {
                var authorizeUrl = this.api.OAuth2.GetAuthorizationUrl(scope, state, redirectUrl);
                this.ViewBag.Url = authorizeUrl;
            }
            else
            {
                this.ViewBag.Url = null;
            }

            var accessToken = "";
            
            this.data.SaveAccessToken(accessToken);


            // step 3
            if (this.data.HasAccessToken)
            {
                var token = this.data.GetAccessToken();
                this.ViewBag.Token = token;
                var user = new UserAuthorization(token);

                var watch = new Stopwatch();
                watch.Start();
                try
                {
                    var acceptLanguages = new string[] { culture ?? "en-US", "fr-FR", };
                    var fields = FieldSelector.For<Person>()
                        .WithAllFields();
                    var profile = await this.api.Profiles.GetMyProfileAsync(user, acceptLanguages, fields);
                    this.ViewBag.Profile = profile;
                }
                catch (LinkedInApiException ex)
                {
                    this.ViewBag.ProfileError = ex.ToString();
                }
                catch (Exception ex)
                {
                    this.ViewBag.ProfileError = ex.ToString();
                }

                watch.Stop();
                this.ViewBag.ProfileDuration = watch.Elapsed;
            }

            return this.View();
        }

        
        public async Task<ActionResult> OAuth2(string code, string state, string error, string error_description)
        {
            if (!string.IsNullOrEmpty(error))
            {
                this.ViewBag.Error = error;
                this.ViewBag.ErrorDescription = error_description;
                return this.View();
            }

            var redirectUrl = this.Request.Compose() + this.Url.Action("OAuth2");
            var result = await this.api.OAuth2.GetAccessTokenAsync(code, redirectUrl);

            this.ViewBag.Code = code;
            this.ViewBag.Token = result.AccessToken;

            this.data.SaveAccessToken(result.AccessToken);

            var user = new UserAuthorization(result.AccessToken);

            ////var profile = this.api.Profiles.GetMyProfile(user);
            ////this.data.SaveAccessToken();
            return this.View();
        }

        public ActionResult Connections()
        {
            var token = this.data.GetAccessToken();
            var user = new UserAuthorization(token);
            // var connection = this.api.Profiles.GetMyConnections(user, 0, 500);
            return this.View(string.Empty);
        }

        public ActionResult FullProfile(string id, string culture = "en-US")
        {
            var token = this.data.GetAccessToken();
            this.ViewBag.Token = token;
            var user = new UserAuthorization(token);

            Person profile = null;
            var watch = new Stopwatch();
            watch.Start();
            try
            {
                ////var profile = this.api.Profiles.GetMyProfile(user);
                var acceptLanguages = new string[] { culture ?? "en-US", "fr-FR", };
                var fields = FieldSelector.For<Person>()                   
                    .WithAllFields();
                profile = this.api.Profiles.GetMyProfileAsync(user, acceptLanguages, fields).Result;

                this.ViewBag.Profile = profile;
            }
            catch (LinkedInApiException ex)
            {
                this.ViewBag.ProfileError = ex.ToString();
                this.ViewBag.RawResponse = ex.Data["ResponseText"];
            }
            catch (LinkedInNetException ex)
            {
                this.ViewBag.ProfileError = ex.ToString();
                this.ViewBag.RawResponse = ex.Data["ResponseText"];
            }
            catch (Exception ex)
            {
                this.ViewBag.ProfileError = ex.ToString();
            }

            watch.Stop();
            this.ViewBag.ProfileDuration = watch.Elapsed;

            return this.View(profile);
        }

        public ActionResult Play()
        {
            var token = this.data.GetAccessToken();
            this.ViewBag.Token = token;
            return this.View();
        }

        public ActionResult Definition()
        {
            var filePath = Path.Combine(this.Server.MapPath("~"), "..", "LinkedInApiV2.xml");
            var builder = new Sparkle.LinkedInNET.ServiceDefinition.ServiceDefinitionBuilder();
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                builder.AppendServiceDefinition(fileStream);
            }

            var result = new ApiResponse<Sparkle.LinkedInNET.ServiceDefinition.ApisRoot>(builder.Root);

            
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new System.IO.StreamWriter(stream);
            var generator = new ServiceDefinition.CSharpGenerator(writer);
            generator.Run(builder.Definition);
            stream.Seek(0L, SeekOrigin.Begin);            
            var serviceResult = new StreamReader(stream).ReadToEnd();


            return this.Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult LogOff(string ReturnUrl)
        {
            this.data.ClearAccessToken();

            if (this.Url.IsLocalUrl(ReturnUrl))
            {
                return this.Redirect(ReturnUrl);
            }
            else
            {
                return this.RedirectToAction("Index");
            }
        }

        public class ApiResponse<T>
        {
            public ApiResponse()
            {
            }

            public ApiResponse(T data)
            {
                this.Data = data;
            }

            public string Error { get; set; }
            public T Data { get; set; }
        }
        
        #region profiles ApiGroup
        private async Task<Person> GetMyProfileAsync(UserAuthorization user)
        {
            try
            {
                string culture = "en-US";
                var acceptLanguages = new string[] { culture ?? "en-US", "fr-FR", };
                var fields = FieldSelector.For<Person>().WithAllFields();
                var profile = await api.Profiles.GetMyProfileAsync(user, acceptLanguages, fields);

                return profile;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<Person> GetProfileAsync(UserAuthorization user, string profileId)
        {
            try
            {
                var profile = await api.Profiles.GetProfileAsync(user, profileId);
                return profile;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<PersonList> GetProfilesByIdsAsync(UserAuthorization user, string profileIds)
        {
            try
            {
                var profiles = await api.Profiles.GetProfilesByIdsAsync(user, profileIds);
                return profiles;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<DegreeSize> GetFirstDegreeConnectionsAsync(UserAuthorization user, string profileId)
        {
            try
            {
                var degreeSize = await api.Profiles.GetFirstDegreeConnectionsAsync(user, profileId);
                return degreeSize;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }
        #endregion

        #region Ugc
        private async Task<UGCPostItems> GetUgcPostsAsync(UserAuthorization user, string companyUrn)
        {
            try
            {
                var posts = await api.UGCPost.GetUGCPostsAsync(user, companyUrn);
                return posts;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<string> Publish_Video_Ugc(UserAuthorization user, string ownerUrn, string mediaUrl)
        {
            try
            {
                var videoData = DownladFromUrlToByte(mediaUrl);
                var video = await Video.VideoUpload.UploadVideoAsync(api, user, ownerUrn, videoData);

                //video test
                var ugcPost = new UGCPost.UGCPostData()
                {
                    Author = ownerUrn,
                    LifecycleState = "PUBLISHED",

                    SpecificContent = new UGCPost.SpecificContent()
                    {
                        ComLinkedinUgcShareContent = new UGCPost.ComLinkedinUgcShareContent()
                        {
                            UGCMedia = new List<UGCPost.UGCMedia>()
                            {
                                new UGCPost.UGCMedia()
                                {
                                    UGCMediaDescription = new UGCPost.UGCText()
                                    {
                                        Text = "Description"
                                    },
                                    Media = video,
                                    Status = "READY",
                                    Thumbnails = new List<ImageThumbnail>(){
                                            new ImageThumbnail()
                                            {
                                                Url = "https://www.google.com/images/branding/googlelogo/2x/googlelogo_color_92x30dp.png",
                                                Height = 500,
                                                Width = 300
                                            }
                                    },
                                    UGCMediaTitle = new UGCPost.UGCText()
                                    {
                                        Text = "Title"
                                    }
                                }
                            },
                            ShareCommentary = new UGCPost.UGCText()
                            {
                                Text = "New video with Thumbnails 2"
                            },
                            ShareMediaCategory = "VIDEO"
                        }
                    },
                    Visibility = new UGCPost.UGCPostvisibility()
                    {
                        comLinkedinUgcMemberNetworkVisibility = "PUBLIC"
                    }
                };

                var t = JsonConvert.SerializeObject(ugcPost);

                var ugcPostResult = await api.UGCPost.PostAsync(user, ugcPost);
                return ugcPostResult;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        #endregion

        #region Shares
        private async Task<PostShares> GetSharePostsAsync(UserAuthorization user, string companyUrn)
        {
            try
            {
                var posts = await api.Shares.GetSharesAsync(user, companyUrn);
                return posts;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<string> Publish_Status_Share(UserAuthorization user, string ownerUrn)
        {
            try
            {
                var text = new PostShareText()
                {
                    Annotations = null,
                    Text = $"Publish Status test from { DateTime.Now}"
                };

                var postItem = new PostShare()
                {
                    Distribution = new Distribution()
                    {
                        LinkedInDistributionTarget = new LinkedInDistributionTarget()
                        {
                            VisibleToGuest = true
                        }
                    },
                    Owner = ownerUrn,
                    Text = text
                };

                var response = await api.Shares.PostAsync(user, postItem);
                var postUrn = "urn:li:share:" + response.Id;

                return postUrn;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<string> Publish_Link_Share(UserAuthorization user, string ownerUrn)
        {
            try
            {
                var text = new PostShareText()
                {
                    Annotations = null,
                    Text = $"Publish Link test from { DateTime.Now}"
                };

                var postItem = new PostShare()
                {
                    Content = new PostShareContent()
                    {
                        Title = "Test title",
                        ContentEntities = new List<PostShareContentEntities>() {
                            new PostShareContentEntities() {
                                EntityLocation = "https://yandex.ru/",
                                Thumbnails = new List<PostShareContentThumbnails>() {
                                    new PostShareContentThumbnails() {
                                        ResolvedUrl = "https://www.google.com/images/branding/googleg/1x/googleg_standard_color_128dp.png",
                                        ImageSpecificContent = {}
                                    }
                                }
                            }
                        }
                    },
                    Distribution = new Distribution()
                    {
                        LinkedInDistributionTarget = new LinkedInDistributionTarget()
                        {
                            VisibleToGuest = true
                        }
                    },
                    Subject = " Test Description",
                    Owner = ownerUrn,
                    Text = text
                };

                var response = await api.Shares.PostAsync(user, postItem);
                var postUrn = "urn:li:share:" + response.Id;

                return postUrn;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<string> Publish_Image_Share(UserAuthorization user, string ownerUrn, string mediaUrl)
        {
            try
            {
                var imagesData = DownladFromUrlToByte(mediaUrl);

                var text = new PostShareText()
                {
                    Annotations = null,
                    Text = $"Publish Image test from { DateTime.Now}"
                };

                var postItem = new PostShare()
                {
                    Distribution = new Distribution()
                    {
                        LinkedInDistributionTarget = new LinkedInDistributionTarget()
                        {
                            VisibleToGuest = true
                        }
                    },
                    Owner = ownerUrn,
                    Text = text
                };

                if (imagesData != null)
                {
                    var contentEntities = new List<PostShareContentEntities>();


                    var imageUrn = await UploadImage(user, imagesData, ownerUrn);

                    if (imageUrn == null)
                    {
                        return null;
                    }

                    contentEntities.Add(new PostShareContentEntities()
                    {
                        Entity = imageUrn
                    });

                    postItem.Content = new PostShareContent()
                    {
                        ContentEntities = contentEntities,
                        MediaCategory = "IMAGE"
                    };
                }

                var response = await api.Shares.PostAsync(user, postItem);
                var postUrn = "urn:li:share:" + response.Id;

                return postUrn;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<string> Publish_Article_Share(UserAuthorization user, string ownerUrn, string mediaUrl)
        {
            try
            {
                var imagesData = DownladFromUrlToByte(mediaUrl);

                var text = new PostShareText()
                {
                    Annotations = null,
                    Text = $"Publish Document test from { DateTime.Now}"
                };

                var postItem = new PostShare()
                {
                    Distribution = new Distribution()
                    {
                        LinkedInDistributionTarget = new LinkedInDistributionTarget()
                        {
                            VisibleToGuest = true
                        }
                    },
                    Owner = ownerUrn,
                    Text = text
                };

                if (imagesData != null)
                {
                    var contentEntities = new List<PostShareContentEntities>
                    {
                        new PostShareContentEntities()
                        {
                            EntityLocation = mediaUrl
                        }
                    };

                    postItem.Content = new PostShareContent()
                    {
                        ContentEntities = contentEntities,
                        MediaCategory = "ARTICLE"
                    };
                }

                var response = await api.Shares.PostAsync(user, postItem);
                var postUrn = "urn:li:share:" + response.Id;

                return postUrn;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        #endregion

        #region Statistics and Analytics
        private async Task<StareStatistic> GetShareStatisticsAsync(UserAuthorization user, string companyId, string shareId)
        {
            try
            {
                var shareStatistics = await api.Shares.GetShareStatisticsAsync(user, companyId, shareId);
                return shareStatistics;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<PostStatistic> GetSharePostStatisticsAsync(UserAuthorization user, string companyId, string shareUrn)
        {
            try
            {
                var sharePostStatistics = await api.Shares.GetSharePostStatisticsAsync(user, companyId, shareUrn);
                return sharePostStatistics;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<VideoAnalytics> GetVideoStatisticsAsync(UserAuthorization user, string videoPostId, string type, string agregation)
        {
            try
            {
                var videoStatistics = await api.Videos.GetVideoStatisticsAsync(user, videoPostId, type, agregation);
                return videoStatistics;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        #endregion

        #region targeting ApiGroup
        private async Task<Industries> GetIndustriesAsync(UserAuthorization user, string language, string country)
        {
            try
            {
                var industries = await api.Targeting.GetIndustriesAsync(user, language, country);

                return industries;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<JobFunctions> GetJobFunctionsAsync(UserAuthorization user, string locale)
        {
            try
            {
                var functions = await api.Targeting.GetJobFunctionsAsync(user, locale);
                return functions;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<CountryGroups> GetCountryGroupsAsync(UserAuthorization user, string language, string country)
        {
            try
            {
                var countryGroups = await api.Targeting.GetCountryGroupsAsync(user, language, country);
                return countryGroups;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<Countries> GetCountriesAsync(UserAuthorization user, string language, string country, string countryGroupUrn)
        {
            try
            {
                var countries = await api.Targeting.GetCountriesAsync(user, language, country, "countryGroup", countryGroupUrn);
                return countries;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<States> GetStatesAsync(UserAuthorization user, string language, string country, string countryUrn)
        {
            try
            {
                var states = await api.Targeting.GetStatesAsync(user, language, country, "country", countryUrn);
                return states;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<Regions> GetRegionsAsync(UserAuthorization user, string language, string country, string stateUrn)
        {
            try
            {
                var regions = await api.Targeting.GetRegionsAsync(user, language, country, "states", stateUrn);
                return regions;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<Seniorities> GetSenioritiesAsync(UserAuthorization user, string language, string country)
        {
            try
            {
                var seniorities = await api.Targeting.GetSenioritiesAsync(user, language, country);
                return seniorities;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<TargetingFacets> GetTargetingFacetsAsync(UserAuthorization user)
        {
            try
            {
                var targetingFacets = await api.Targeting.GetTargetingFacetsAsync(user);
                return targetingFacets;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        private async Task<AudienceCounts> GetAudienceCountsAsync(UserAuthorization user, string targetingCriteria)
        {
            try
            {
                var audienceCounts = await api.Targeting.GetAudienceCountsAsync(user, targetingCriteria);
                return audienceCounts;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }

        #endregion

        #region common upload methods
        public static byte[] DownladFromUrlToByte(string url)
        {
            HttpWebRequest req;
            HttpWebResponse res = null;

            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                res = (HttpWebResponse)req.GetResponse();
                Stream stream = res.GetResponseStream();

                var buffer = new byte[4096];
                using (MemoryStream ms = new MemoryStream())
                {
                    var bytesRead = 0;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }
                    return ms.ToArray();
                }
            }
            finally
            {
                if (res != null)
                {
                    res.Close();
                }
            }
        }

        public async Task<string> UploadImage(UserAuthorization user, byte[] imageData, string ownerUrn)
        {
            try
            {
                var asset = new Asset.RegisterUploadRequest()
                {
                    RegisterUploadRequestData = new Asset.RegisterUploadRequestData()
                    {
                        Owner = ownerUrn,
                        Recipes = new List<string>() { "urn:li:digitalmediaRecipe:feedshare-image" },
                        ServiceRelationships = new List<Asset.ServiceRelationship>()
                        {
                            new ServiceRelationship()
                            {
                                Identifier = "urn:li:userGeneratedContent",
                                RelationshipType = "OWNER"
                            }
                        }
                    }
                };
                var requestAsset = await api.Asset.RegisterUploadAsync(user, asset);
                var imageUrn = requestAsset.Value.Asset;

                var postAsset = await api.Asset.UploadImageAssetAsync(user,
                    requestAsset.Value.UploadMechanism.ComLinkedinDigitalmediaUploadingMediaUploadHttpRequest.UploadUrl,
                    new Asset.UploadAssetRequest()
                    {
                        RequestHeaders = new Asset.ComLinkedinDigitalmediaUploadingMediaUploadHttpRequest()
                        {
                            Headers = requestAsset.Value.UploadMechanism.ComLinkedinDigitalmediaUploadingMediaUploadHttpRequest.Headers,
                            UploadUrl = requestAsset.Value.UploadMechanism.ComLinkedinDigitalmediaUploadingMediaUploadHttpRequest.UploadUrl,
                        },
                        Data = imageData

                    });

                return imageUrn;
            }
            catch (LinkedInApiException ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
            catch (Exception ex)
            {
                errors.Add(ex.ToString());
                return null;
            }
        }
        #endregion
        
    }
}
