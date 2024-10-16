import puppeteer from "puppeteer-core";
import { load } from "cheerio";
import fs from "fs";
import path from "path";
import axios from "axios";
import { CookieJar } from "tough-cookie";
import { wrapper } from "axios-cookiejar-support";

// Function to sanitize the filename
function sanitizeFilename(filename) {
  return filename.replace(/[<>:"\/\\|?*]+/g, "_"); // Replace invalid characters with underscores
}

async function downloadFile(downloadLink, lessonFolder, fileName) {
  try {
    const jar = new CookieJar();
    const axiosInstance = wrapper(axios.create({ jar }));

    // Initial request to set cookies
    const initialResponse = await axiosInstance.get(downloadLink, {
      headers: {
        "User-Agent":
          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.81 Safari/537.36",
      },
    });

    if (initialResponse.status !== 200) {
      console.error(
        `Initial request failed with status: ${initialResponse.status}`
      );
      return;
    }

    const fileUrl = initialResponse.request.res.responseUrl;

    // Sanitize the filename and set the file path
    const sanitizedFileName =
      sanitizeFilename(fileName) + path.extname(fileUrl); // Get file extension
    const filePath = path.join(lessonFolder, sanitizedFileName);

    const fileResponse = await axiosInstance.get(fileUrl, {
      responseType: "stream",
      headers: {
        "User-Agent":
          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.81 Safari/537.36",
      },
    });

    const dest = fs.createWriteStream(filePath);
    fileResponse.data.pipe(dest);

    dest.on("finish", () => {
      console.log(`Downloaded: ${filePath}`);
    });

    dest.on("error", (err) => {
      console.error(`Error writing file: ${err}`);
    });
  } catch (error) {
    console.error(`Failed to download ${downloadLink}: ${error.message}`);
  }
}

async function scrapeExamQuestions() {
  const browser = await puppeteer.launch({
    executablePath: "/usr/bin/google-chrome",
  });

  const mainPage = await browser.newPage();
  await mainPage.goto("https://www.kanoon.ir/Public/ExamQuestions", {
    waitUntil: "networkidle2",
  });

  const mainHtml = await mainPage.content();
  const $ = load(mainHtml);

  const curriculumLinks = [];

  $("ul.list-group").each((index, ulElement) => {
    $(ulElement)
      .find("li")
      .each((liIndex, liElement) => {
        const anchorElement = $(liElement).find("a");
        if (anchorElement.length > 0) {
          const href = anchorElement.attr("href");
          const text = anchorElement.text().trim();
          curriculumLinks.push({ href: `https://www.kanoon.ir${href}`, text });
        }
      });
  });

  for (const {
    href: curriculumLink,
    text: curriculumName,
  } of curriculumLinks) {
    const curriculumPage = await browser.newPage();
    await curriculumPage.goto(curriculumLink, {
      waitUntil: "networkidle2",
    });

    const curriculumHtml = await curriculumPage.content();
    const curriculum$ = load(curriculumHtml);

    const lessonLinks = [];
    curriculum$("a.list-group-item").each((index, anchorElement) => {
      const lessonHref = curriculum$(anchorElement).attr("href");
      const lessonName = curriculum$(anchorElement)
        .find(".LessonName")
        .text()
        .trim();

      lessonLinks.push({
        href: `https://www.kanoon.ir${lessonHref}`,
        name: lessonName,
      });
    });

    const curriculumFolder = path.join(
      "/home/f.ahmadi/Desktop/Node-Scraper/",
      "downloads",
      curriculumName
    );
    fs.mkdirSync(curriculumFolder, { recursive: true });

    const lessonLinksToProcess = lessonLinks.slice(0, -1); // Exclude the last lesson link

    for (const { href: lessonLink, name: lessonName } of lessonLinksToProcess) {
      const lessonPage = await browser.newPage();
      await lessonPage.goto(lessonLink, {
        waitUntil: "networkidle2",
      });

      const lessonHtml = await lessonPage.content();
      const lesson$ = load(lessonHtml);

      const downloadLinks = [];
      lesson$("a.downloadfile").each((index, anchorElement) => {
        const downloadHref = lesson$(anchorElement).attr("href");
        const fileName = lesson$(anchorElement).find("div").text().trim(); // Get the title from the div
        downloadLinks.push({
          href: `https://www.kanoon.ir${downloadHref}`,
          name: fileName,
        });
      });

      const lessonFolder = path.join(curriculumFolder, lessonName);
      fs.mkdirSync(lessonFolder, { recursive: true });

      // Download each file using node-fetch
      for (const { href: downloadLink, name: fileName } of downloadLinks) {
        try {
          await downloadFile(downloadLink, lessonFolder, fileName);
        } catch (error) {
          console.error(`Failed to download ${downloadLink}: ${error.message}`);
        }
      }
      await lessonPage.close();
    }

    await curriculumPage.close();
  }

  await browser.close();
}

// Run the function
scrapeExamQuestions();
