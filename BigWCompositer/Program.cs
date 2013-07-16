using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.IO;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;


namespace BigWCompositer
{
    class Program
    {
        // config settings
        private static NameValueCollection appConfig;

        // Amazon S3
        private static string AWSAccessKeyID;
        private static string AWSSecretAccessKeyID;

        // File paths
        private static string folderPath;
        private static string baseVideoPath;
        private static string imagePath;
        private static string outputFolderPath;
        private static string processingFolderPath;
        private static string urlPath;
        private static string ffmpegPath;
        private static string fileSuffix;
        private static string[] files;
        private static string _filePathAndName;
        private static string _fileNameNoExtension;
        private static string fileName;
        private static int numberOfFilesToProcess;
        // FFMPEG helper object
        private static Converter _converter;

        // for debugging
        private static bool _debug;

        static void Main(string[] args)
        {

            if (!checkRequiredFields()) {
                WaitForKey(_debug);
                return;
            }

            // Get the first matching movie and text file from S3 and then delete them
            numberOfFilesToProcess = GetFilesFromS3(_debug);
            if (numberOfFilesToProcess < 0) {
                WaitForKey(_debug);
                return;
            }

            while (numberOfFilesToProcess >= 0) {

                // Get the first file (.mp4 and .txt matching) from the NewVideos folder
                // Move them to the processing folder
                string fileBeingProcessed = GetFirstFileFromFolder();

                // get the meta data from the associated txt file
                string[] metaData = GetMetaData(fileBeingProcessed);

                // decide from Mode in metaData whether to just concatenate or to rotate/merge/concatenate
                bool rotate = CheckFileIsPortrait(metaData);

                // Now process the videos and move the files and result to Complete folder
                if (fileBeingProcessed != null) {
                    string fileToProcessPath = processingFolderPath + fileBeingProcessed + ".mp4";
                    string url = urlPath + fileBeingProcessed + fileSuffix + "-" + metaData[0] + "-" + metaData[1] + ".mp4";
                    string name = metaData[0] + " " + metaData[1];
                    string emailAddress = metaData[2];
                    int rot;
                    if (rotate) {
                        rot = CheckRotation(metaData);
                        // select the background to use based on metadata
                        string bgImagePath = imagePath + CheckSelectedToy(metaData);
                        // Now rotate/merge/concatenate the videos and move the files and result to Complete folder
                        RotateAndMergeVideos(rot, fileToProcessPath, bgImagePath, fileBeingProcessed, outputFolderPath, fileSuffix, metaData);
                    }
                    else {
                        rot = CheckRotation(metaData);
                        // Now concatenate the videos and move the files and result to Complete folder
                        ConcatenateVideos(rot, baseVideoPath, fileToProcessPath, fileBeingProcessed, outputFolderPath, fileSuffix, metaData);
                    }
                    SendEmail(url, name, emailAddress);

                }

                // Get the first matching movie and text file from S3 and then delete them
                numberOfFilesToProcess = GetFilesFromS3(_debug);

                WaitForKey(_debug);
            }
        }

        private static bool CheckFileIsPortrait(string[] metaData)
        {
            if (metaData[4].Contains("Portrait")) {
                return true;
            }
            return false;
        }

        private static int CheckRotation(string[] metaData)
        {
            if (metaData[4] == "PortraitUpsideDown") {
                return 2;
            }
            if (metaData[4] == "LandscapeRight") {
                Console.WriteLine("LandscapeRight");
                return 0;
            }
            return 1;

        }

        private static string CheckSelectedToy(string[] metaData)
        {
            string toy = metaData[5];
            if (toy.Contains("TMNT")) {
                return "turtles.png";
            }
            if (toy.Contains("Barbie")) {
                return "barbies.png";
            }
            if (toy.Contains("Monsters")) {
                return "monsters.png";
            }
            if (toy.Contains("Superheroes")) {
                return "superheroes.png";
            }
            return "turtles.png";
        }

        private static string[] GetMetaData(string fileName)
        {
            string fileToProcessPath = processingFolderPath + fileName + ".txt";
            try {
                string[] metaData = File.ReadAllLines(fileToProcessPath);
                foreach (string line in metaData) {
                    Console.WriteLine(line);
                }
                return metaData;
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
            }
            return null;
        }

        private static void WaitForKey(bool wait)
        {
            if (wait) {
                Console.Write("Press any key to continue...");
                Console.ReadKey();
            }
        }

        static bool checkRequiredFields()
        {
            appConfig = ConfigurationManager.AppSettings;

            if (string.IsNullOrEmpty(appConfig["AWSAccessKey"])) {
                Console.WriteLine("AWSAccessKey was not set in the App.config file.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["AWSSecretKey"])) {
                Console.WriteLine("AWSSecretKey was not set in the App.config file.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["folder"])) {
                Console.WriteLine("The folder path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["baseVideoPath"])) {
                Console.WriteLine("The base video path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["imagePath"])) {
                Console.WriteLine("The image path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["outputFolder"])) {
                Console.WriteLine("The output folder path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["processingFolder"])) {
                Console.WriteLine("The processing folder path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["ffmpegPath"])) {
                Console.WriteLine("The ffmpeg exe path is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["fileSuffix"])) {
                Console.WriteLine("The text for the completed video name is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["debug"])) {
                Console.WriteLine("The debug status is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(appConfig["urlPath"])) {
                Console.WriteLine("The url Path is not set.");
                return false;
            }
            // Amazon S3
            AWSAccessKeyID = appConfig["AWSAccessKey"];
            AWSSecretAccessKeyID = appConfig["AWSSecretKey"];

            // File paths
            folderPath = appConfig["folder"];
            baseVideoPath = appConfig["baseVideoPath"];
            imagePath = appConfig["imagePath"];
            outputFolderPath = appConfig["outputFolder"];
            processingFolderPath = appConfig["processingFolder"];
            urlPath = appConfig["urlPath"];
            ffmpegPath = appConfig["ffmpegPath"];
            fileSuffix = appConfig["fileSuffix"];
            _debug = appConfig["debug"] == "true";
            return true;
        }

        private static string GetFirstFileFromFolder()
        {
            files = Directory.GetFiles(@folderPath, "*.mp4");
            if (files.Length > 0) {
                _filePathAndName = files[0];
                fileName = Path.GetFileName(_filePathAndName);
                _fileNameNoExtension = Path.GetFileNameWithoutExtension(_filePathAndName);
                Console.WriteLine(_filePathAndName);
                Console.WriteLine(fileName);
                Console.WriteLine(_fileNameNoExtension);
                MoveFileTo(_filePathAndName, processingFolderPath);
                MoveFileTo(folderPath + _fileNameNoExtension + ".txt", processingFolderPath);
                return _fileNameNoExtension;
            }
            else {
                Console.WriteLine("No files");
                return null;
            }
        }

        private static void ConcatenateVideos(int rot,
                                                string file1Path,
                                                string file2Path,
                                                string fileName,
                                                string outputFolderPath,
                                                string fileSuffix,
                                                string[] metaData)
        {
            _converter = new Converter(ffmpegPath);
            Console.WriteLine("Concatenating videos");
            OutputPackage oo = _converter.ConcatenateVideos(rot, file1Path, file2Path, fileName, outputFolderPath, fileSuffix, metaData);
            string txtFile = processingFolderPath + fileName + ".txt";
            string movieFile = processingFolderPath + fileName + ".mp4";
            string tempFile1 = movieFile + "-temp1.ts";
            if (rot == 0) {
                string flippedFile = movieFile + "-flipped.mp4";
                File.Delete(flippedFile);
            }
            MoveFileTo(txtFile, outputFolderPath);
            MoveFileTo(movieFile, outputFolderPath);
            File.Delete(tempFile1);
            Console.WriteLine("All processing files moved/deleted.");
        }
        private static void RotateAndMergeVideos(int rot,
                                                string file1Path,
                                                string file2Path,
                                                string fileName,
                                                string outputFolderPath,
                                                string fileSuffix,
                                                string[] metaData)
        {
            _converter = new Converter(ffmpegPath);
            Console.WriteLine("Rotating videos");
            OutputPackage oo = _converter.RotateAndMergeVideo(rot, file1Path, baseVideoPath, file2Path, fileName, outputFolderPath, fileSuffix, metaData);
            string txtFile = processingFolderPath + fileName + ".txt";
            string movieFile = processingFolderPath + fileName + ".mp4";
            string tempFile1 = movieFile + "-temp1.ts";
            string mergeFile1 = movieFile + "-merged.mp4";
            MoveFileTo(txtFile, outputFolderPath);
            MoveFileTo(movieFile, outputFolderPath);
            File.Delete(tempFile1);
            File.Delete(mergeFile1);
            Console.WriteLine("All processing files moved/deleted.");
        }

        private static void MoveFileTo(string filePathAndName, string destinationPath)
        {
            string fileName = Path.GetFileName(filePathAndName);
            string destPathAndName = destinationPath + fileName;
            try {
                // Ensure that the target does not exist. 
                if (File.Exists(destPathAndName))
                    File.Delete(destPathAndName);

                // Move the file.
                File.Move(filePathAndName, destPathAndName);
                Console.WriteLine("{0} was moved to {1}.", filePathAndName, destPathAndName);

                // See if the original exists now. 
                if (File.Exists(filePathAndName)) {
                    Console.WriteLine("The original file still exists, which is unexpected.");
                }
                else {
                    Console.WriteLine("The original file no longer exists, which is expected.");
                }

            }
            catch (Exception e) {
                Console.WriteLine("The process failed: {0}", e.ToString());
            }
        }

        private static void SendEmail(string url, string Name, string emailAddress)
        {
            if (_debug) {
                return;
            }
            const String FROM = "bigw@maverick.com.au";  // Replace with your "From" address. This address must be verified.
            string to = emailAddress; // Replace with a "To" address. If you have not yet requested
            // production access, this address must be verified.
            string subjectText = "BigW: Your Giant Toy Spectacular VideoClip";
            string bodyText = "Giant Toy Spectaculer | Big W" +
                              "\n " + "Thanks for taking part in The Big W Toy Spectacular video activity!" +
                              "\n " +
                              "You have now been entered for your chance to become the Deputy of the Big W Toy Army and win a $500 Big W gift card to spend on building your toy army!" +
                              "\n " +
                              "Your video can be downloaded from here: " + 
                              url +
                              ", it looks great! Why don't you share it with your friends and family on Facebook?" +
                              "\n " +
                              "Winners will be notified via phone and email on 9th July 2013. Good luck! All details are held in accordance with the Terms and Conditions. (see below)" +
                              "\n " + "Big W Giant Toy Spectacular –" +
                              "\n " + "In Store 27th June – 10th July 2013" +
                              "\n " + "or online www.bigw.com.au" +
                              "\n" + "Terms & Conditions" +
                                "\n" + "1 Information on how to enter forms part of the terms of entry. Entry into the Promotion is deemed acceptance of these terms and conditions.2 The Promoter is Woolworths Limited (ABN 88 000 014 675) of 1 Woolworths Way, Bella Vista New South Wales 2153.3 The Promotion commences on 27 June 2013 at 10.00am (AEST) and closes on 6 July 2013 at 5.00 pm (AEST)." +
                                "\n" +
                                "\n" + "How to enter" +
                                "\n" + "4 Entry is open to Australian residents. Employees of the Promoter or any related body corporate and their immediate families are not eligible to enter.5 Entrants under the age of 18 years must have parental/guardian approval to enter. The parent/guardian agrees to the terms and conditions of the Promotion.6 To enter, eligible entrants must:(a) have their video captured by the Promoter's promotional staff at any of the following events during the specified period:(i) Westfield Penrith, 585 High Street, Penrith New South Wales, on:(A) 27 June 2013 between 9.00am and 9.00pm;(B) 28 June 2013 between 9.00am and 5.30pm; and(C) 29 June 2013 between 9.00am and 5.00pm;(ii) Westfield Southland, 1239 Nepean Highway, Cheltenham Victoria, on:(A) 27 June 2013 between 9.00am and 9.00pm;(B) 28 June 2013 between 9.00am and 9.00pm; and(C) 29 June 2013 between 9.00am and 5.00pm;(iii) LendLease Macarthur Square, 200 Gilchrist Drive, Campbelltown New South Wales, on:(A) 4 July 2013 between 9.00am and 9.00pm;(B) 5 July 2013 between 9.00am and 5.30pm; and(C) 6 July 2013 between 9.00am and 5.30pm; and(iv) Westfield Fountain Gate, 352 Princes Highway, Fountain Gate Victoria, on:(A) 4 July 2013 between 9.00am and 9.00pm;(B) 5 July 2013 between 9.00am and 9.00pm; and(C) 6 July 2013 between 9.00am and 5.00pm;(b) register their details including name and email address with the Promoter's promotional staff; and(c) successfully receive an email containing their video from the Promoter.7 Multiple entries are allowed. More than one entrant with a valid email address is allowed per video capture.8 By entering the Promotion, eligible entrants acknowledge that their entry becomes the property of the Promoter.9 Incomplete or non-conforming entries will be deemed invalid." +
                                "\n" +
                                "\n" + "Prizes and judging" +
                                "\n" + "10 This is a game of chance.11 There will be one prize: a AUD$500 Big W Voucher. The prize is subject to Big W's standard terms and conditions for use and redemption of gift vouchers, including expiry.12 Total prize pool is valued at AUD$500.00 (incl. GST).13 The winner will be decided at random by electronic draw. The draw will take place on 8 July 2013 at 10.00am (AEST) at Ground Floor, 7 Northcliff Street, Milsons Point NSW.14 The winner will be notified via phone and email on 9 July 2013.15 If the winner is disqualified in accordance with these terms and conditions or the prize remains unclaimed after the Promoter has made reasonable efforts to contact the winner, the winner will forfeit the prize and an unclaimed draw prize will take place on 9 October 2013 at the same place as the original draw, subject to any directions from a regulatory authority. The winners, if any, will be notified via phone and email on 10 October 2013.16 The voucher will be delivered to the winner within 7 days of the date of the draw.17 By accepting the prize, the winner (and their parent/guardian) agrees to participate in and co-operate as required with all reasonable marketing activities relating to the prize.18 The Promoters decision is final and the Promoter will not enter into correspondence regarding the competition result." +
                                "\n" +
                                "\n" + "General" +
                                "\n" + "19 The prize, or any unused portion of the prize, cannot be exchanged or redeemed for cash. If a prize is unavailable for whatever reason, the Promoter reserves the right to substitute the prize for a prize of equal or greater value.20 The Promoter reserves the right, at any time, and in its sole discretion to (a) request entrants to provide proof of identity and/or proof of valid entry (b) disqualify any entry that it considers to be illegal, discriminatory, offensive or otherwise inappropriate (c) disqualify any entrant who the Promoter has reason to believe has breached any of these conditions or engaged in any unlawful or other improper conduct or any conduct calculated to jeopardise the fair and proper conduct of the promotion.21 If for any reason this competition is not capable of running as planned, including but not limited to tampering, unauthorised intervention, fraud, any technical difficulties or equipment malfunction or any causes beyond the control of the Promoter, which corrupt or affect the administration, security, fairness or integrity or proper conduct of this promotion, the Promoter reserves the right in its sole discretion to take any action that may be available, and to cancel, terminate, modify or suspend the competition.22 The Promoter makes no representations or warranties as to the quality, suitability or merchantability of any goods or services offered as part of the Promotion. To the extent permitted by law, the Promoter is not liable for any loss (including indirect and consequential loss) suffered to person or property by reason of any act or omission (including deliberate or negligent acts or omissions) by the Promoter or its employees or agents, in connection with the arrangement for the supply, or the supply, of goods or services by any person to the prize winners and, where applicable to any persons accompanying the prize winners. This clause does not affect any rights a consumer may have which are unable to be excluded under Australian law. To the fullest extent permitted by law, any liability of the Promoter or its employees or agents for breach of any such rights is limited to the payment of the costs of having the prize supplied again.23 Failure of the Promoter to enforce any of its rights at any stage does not constitute a waiver of those rights." +
                                "\n" +
                                "\n" + "Privacy" +
                                "\n" + "24 The Promoter collects the entrants personal information for the purpose of conducting and promoting this competition (including but not limited to determining and notifying the winner). If you are not willing for this to occur you cannot participate in the Promotion.25 By entering the Promotion, unless otherwise advised, each entrant also agrees that the Promoter may use personal information collected to conduct the promotion, in any media for future promotional, marketing and publicity purposes without any further reference, payment or other compensation to the entrant, including sending the entrant electronic messages. The Promoter will hold an entrants' personal information in a secure manner in accordance with the Promoter's Privacy Policy (a copy of which can be found on www.bigw.com.au for an indefinite period). A request to access, update or correct any information should be directed to the Promoter." +
                                "\n" +
                                "\n" + "NSW Permit No. LTPS/13/04675";
            string html = "<div style='width:720px;margin:auto;font-size:120%;font-family:Questrial,Sans-serif;line-height:140%;padding:1em 0;text-align: center;' class='wrapper'><img width='100%' alt='Giant Toy Spectaculer | Big W' src='https://googledrive.com/host/0B5Kao6YDsg50YzZJSU5IMG1xZmc/BigWGiantToy.jpg'><h1 style='color:#00a1e4;font-size:1.2em;padding:1em 0 0.5em;'>Thanks for taking part in The Big W Toy Spectacular video activity!</h1><p>You have now been entered for your chance to become the Deputy of the Big W Toy Army and win a $500 Big W gift card to spend on building your toy army!</p>" + 
                "<p>Your video can be downloaded <a href='" + url + "' target='_blank'>here</a>, it looks great!" + 
                "Why don't you share it with your friends and family on Facebook?</p><p>Winners will be notified via phone and email on 9th July 2013. Good luck! All details are held in accordance with the Terms and Conditions. (see below)</p><p>Big W Giant Toy Spectacular &ndash;<br>In Store 27th June &ndash; 10th July 2013<br>or online <a style='color:#00a1e4;text-decoration:none;' target='_blank' href='http://www.bigw.com.au'>www.bigw.com.au</a></p></div>" +
                "<div style='width:720px;margin:auto;font-size:100%;font-family:Questrial,Sans-serif;line-height:100%;padding:0.8em 0;text-align: center;' class='wrapper'><small style='color:#999;'><p><b>Terms &amp; Conditions</b></p>1	Information on how to enter forms part of the terms of entry.  Entry into the Promotion is deemed acceptance of these terms and conditions.2	The Promoter is Woolworths Limited (ABN 88 000 014 675) of 1 Woolworths Way, Bella Vista New South Wales 2153.3	The Promotion commences on 27 June 2013 at 10.00am (AEST) and closes on 6 July 2013 at 5.00 pm (AEST).<br><br>How to enter<br>4	Entry is open to Australian residents.  Employees of the Promoter or any related body corporate and their immediate families are not eligible to enter.5	Entrants under the age of 18 years must have parental/guardian approval to enter.  The parent/guardian agrees to the terms and conditions of the Promotion.6	To enter, eligible entrants must:(a)	have their video captured by the Promoter&trade;'s promotional staff at any of the following events during the specified period:(i)	Westfield Penrith, 585 High Street, Penrith New South Wales, on:(A)	27 June 2013 between 9.00am and 9.00pm;(B)	28 June 2013 between 9.00am and 5.30pm; and(C)	29 June 2013 between 9.00am and 5.00pm;(ii)	Westfield Southland, 1239 Nepean Highway, Cheltenham Victoria, on:(A)	27 June 2013 between 9.00am and 9.00pm;(B)	28 June 2013 between 9.00am and 9.00pm; and(C)	29 June 2013 between 9.00am and 5.00pm;(iii)	LendLease Macarthur Square, 200 Gilchrist Drive, Campbelltown New South Wales, on:(A)	4 July 2013 between 9.00am and 9.00pm;(B)	5 July 2013 between 9.00am and 5.30pm; and(C)	6 July 2013 between 9.00am and 5.30pm; and(iv)	Westfield Fountain Gate, 352 Princes Highway, Fountain Gate Victoria, on:(A)	4 July 2013 between 9.00am and 9.00pm;(B)	5 July 2013 between 9.00am and 9.00pm; and(C)	6 July 2013 between 9.00am and 5.00pm;(b)	register their details including name and email address with the Promoter&trade;'s promotional staff; and(c)	successfully receive an email containing their video from the Promoter.7	Multiple entries are allowed.  More than one entrant with a valid email address is allowed per video capture.8	By entering the Promotion, eligible entrants acknowledge that their entry becomes the property of the Promoter.9	Incomplete or non-conforming entries will be deemed invalid.<br><br>Prizes and judging<br>10	This is a game of chance.11	There will be one prize: a AUD$500 Big W Voucher.  The prize is subject to Big W&trade;'s standard terms and conditions for use and redemption of gift vouchers, including expiry.12	Total prize pool is valued at AUD$500.00 (incl. GST).13	The winner will be decided at random by electronic draw.  The draw will take place on 8 July 2013 at 10.00am (AEST) at Ground Floor, 7 Northcliff Street, Milsons Point NSW.14	The winner will be notified via phone and email on 9 July 2013.15	If the winner is disqualified in accordance with these terms and conditions or the prize remains unclaimed after the Promoter has made reasonable efforts to contact the winner, the winner will forfeit the prize and an unclaimed draw prize will take place on 9 October 2013 at the same place as the original draw, subject to any directions from a regulatory authority.   The winners, if any, will be notified via phone and email on 10 October 2013.16	The voucher will be delivered to the winner within 7 days of the date of the draw.17	By accepting the prize, the winner (and their parent/guardian) agrees to participate in and co-operate as required with all reasonable marketing activities relating to the prize.18	The Promoter's decision is final and the Promoter will not enter into correspondence regarding the competition result.<br><br>General<br>19	The prize, or any unused portion of the prize, cannot be exchanged or redeemed for cash.  If a prize is unavailable for whatever reason, the Promoter reserves the right to substitute the prize for a prize of equal or greater value.20	The Promoter reserves the right, at any time, and in its sole discretion to (a) request entrants to provide proof of identity and/or proof of valid entry (b) disqualify any entry that it considers to be illegal, discriminatory, offensive or otherwise inappropriate (c) disqualify any entrant who the Promoter has reason to believe has breached any of these conditions or engaged in any unlawful or other improper conduct or any conduct calculated to jeopardise the fair and proper conduct of the promotion.21	If for any reason this competition is not capable of running as planned, including but not limited to tampering, unauthorised intervention, fraud, any technical difficulties or equipment malfunction or any causes beyond the control of the Promoter, which corrupt or affect the administration, security, fairness or integrity or proper conduct of this promotion, the Promoter reserves the right in its sole discretion to take any action that may be available, and to cancel, terminate, modify or suspend the competition.22	The Promoter makes no representations or warranties as to the quality, suitability or merchantability of any goods or services offered as part of the Promotion.  To the extent permitted by law, the Promoter is not liable for any loss (including indirect and consequential loss) suffered to person or property by reason of any act or omission (including deliberate or negligent acts or omissions) by the Promoter or its employees or agents, in connection with the arrangement for the supply, or the supply, of goods or services by any person to the prize winners and, where applicable to any persons accompanying the prize winners.  This clause does not affect any rights a consumer may have which are unable to be excluded under Australian law.  To the fullest extent permitted by law, any liability of the Promoter or its employees or agents for breach of any such rights is limited to the payment of the costs of having the prize supplied again.23	Failure of the Promoter to enforce any of its rights at any stage does not constitute a waiver of those rights.<br><br>Privacy<br>24	The Promoter collects the entrant's personal information for the purpose of conducting and promoting this competition (including but not limited to determining and notifying the winner). If you are not willing for this to occur you cannot participate in the Promotion.25	By entering the Promotion, unless otherwise advised, each entrant also agrees that the Promoter may use personal information collected to conduct the promotion, in any media for future promotional, marketing and publicity purposes without any further reference, payment or other compensation to the entrant, including sending the entrant electronic messages.  The Promoter will hold an entrants'&trade; personal information in a secure manner in accordance with the Promoter&trade;'s Privacy Policy (a copy of which can be found on www.bigw.com.au for an indefinite period).  A request to access, update or correct any information should be directed to the Promoter.<br><br>NSW Permit No. LTPS/13/04675</small> </div>";
            // Construct an object to contain the recipient address.
            Destination destination = new Destination().WithToAddresses(new List<string>() { to });

            // Create the subject and body of the message.
            Content subject = new Content().WithData(subjectText);
            Content textBody = new Content().WithData(bodyText);
            Content htmlContent = new Content().WithData(html);
            Body body = new Body().WithHtml(htmlContent).WithText(textBody);

            // Create a message with the specified subject and body.
            Message message = new Message().WithSubject(subject).WithBody(body);

            // Assemble the email.
            SendEmailRequest request = new SendEmailRequest().WithSource(FROM).WithDestination(destination).WithMessage(message);

            // Instantiate an Amazon SES client, which will make the service call. Since we are instantiating an 
            // AmazonSimpleEmailServiceClient object with no parameters, the constructor looks in App.config for 
            // your AWS credentials by default. When you created your new AWS project in Visual Studio, the AWS
            // credentials you entered were added to App.config.
            AmazonSimpleEmailServiceClient client = new AmazonSimpleEmailServiceClient();

            // Send the email.
            try {
                Console.WriteLine("Attempting to send an email through Amazon SES by using the AWS SDK for .NET...");
                client.SendEmail(request);
                Console.WriteLine("Email sent!");
            }
            catch (Exception ex) {
                Console.WriteLine("The email was not sent.");
                Console.WriteLine("Error message: " + ex.Message);
            }
        }

        private static int GetFilesFromS3(bool keepFiles)
        {
            string bucketName = "as-bigw";
            string fileName;
            string fileNameWithNoExtension;
            string movieFile;
            string txtFile;
            string movieDestName;
            string txtDestName;
            int numberOfFiles;
            using (AmazonS3 client = Amazon.AWSClientFactory.CreateAmazonS3Client(AWSAccessKeyID, AWSSecretAccessKeyID)) {

                ListObjectsRequest listObjectsRequest = new ListObjectsRequest().WithBucketName(bucketName);

                // List all objects
                ListObjectsRequest listRequest = new ListObjectsRequest().WithBucketName(bucketName);
                ListObjectsResponse listResponse = client.ListObjects(listRequest);
                if (listResponse.S3Objects.Count() < 2) {
                    Console.WriteLine("There are no files in the Amazon S3 Bucket.");
                    return -1;
                }
                numberOfFiles = listResponse.S3Objects.Count();
                fileName = listResponse.S3Objects[0].Key;
                fileNameWithNoExtension = fileName.Split('.')[0];
                movieFile = fileNameWithNoExtension + ".mp4";
                txtFile = fileNameWithNoExtension + ".txt";
                Console.WriteLine(fileName);

                movieDestName = appConfig["folder"] + fileNameWithNoExtension + ".mp4";
                txtDestName = appConfig["folder"] + fileNameWithNoExtension + ".txt";
                Console.WriteLine(movieDestName + " :: " + txtDestName);

                try {
                    GetObjectRequest getObjectRequest =
                        new GetObjectRequest().WithBucketName(bucketName).WithKey(movieFile);
                    using (S3Response getObjectResponse = client.GetObject(getObjectRequest)) {
                        if (!File.Exists(movieDestName)) {
                            using (Stream s = getObjectResponse.ResponseStream) {
                                using (FileStream fs = new FileStream(movieDestName, FileMode.Create, FileAccess.Write)) {
                                    byte[] data = new byte[32768];
                                    int bytesRead = 0;
                                    do {
                                        bytesRead = s.Read(data, 0, data.Length);
                                        fs.Write(data, 0, bytesRead);
                                    } while (bytesRead > 0);
                                    fs.Flush();
                                }
                            }
                        }
                    }
                    if (getObjectRequest == null) {
                        return -1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return -1;
                }

                try {
                    GetObjectRequest getObjectRequest2 =
                        new GetObjectRequest().WithBucketName(bucketName).WithKey(txtFile);
                    if (getObjectRequest2 == null) {
                        return -1;
                    }
                    using (S3Response getObjectResponse2 = client.GetObject(getObjectRequest2)) {
                        if (!File.Exists(txtDestName)) {
                            using (Stream s = getObjectResponse2.ResponseStream) {
                                string text = "";
                                using (StreamReader reader = new StreamReader(s)) {
                                    text = reader.ReadToEnd();
                                }

                                using (StreamWriter sw = File.CreateText(txtDestName)) {
                                    sw.WriteLine(text);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return -1;
                }

                if (!keepFiles) {
                    // Create a DeleteObject request
                    DeleteObjectRequest request = new DeleteObjectRequest().WithBucketName(bucketName).WithKey(movieFile);
                    // Issue request
                    client.DeleteObject(request);
                    // Create a DeleteObject request
                    DeleteObjectRequest request2 = new DeleteObjectRequest().WithBucketName(bucketName).WithKey(txtFile);
                    // Issue request
                    client.DeleteObject(request2);
                    numberOfFiles -= 2;
                }
                return numberOfFiles;
            }
        }
        private static void RestartApplication()
        {
            ProcessStartInfo Info = new ProcessStartInfo();
            Info.Arguments = "/C ping 127.0.0.1 -n 2 && \"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"";
            Info.WindowStyle = ProcessWindowStyle.Hidden;
            Info.CreateNoWindow = true;
            Info.FileName = "cmd.exe";
            Process.Start(Info);
        }

    }
}
