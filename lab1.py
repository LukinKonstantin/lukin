import requests
import regex
import re

def WritePageLinks(urls):
	print()
	print("All links on this page")
	for url in urls:
		if url != mainUrl:
			print(url)
	print()

def WritePageEmails(emails):
	print()
	print("All emails on this page")
	for email in emails:
		print(email)
	print()

def AddNewMails(emails):
	global allMails
	for somemail in emails:
		if somemail not in allMails:
			allMails.append(somemail)

def GetPageInfo(url):
	print("Now we on next page:", url)
	websiteText = requests.get(url)
	# Получить все ссылки
	urls = list(set(regex.findall(r'href=[\'"]?([^\'" >]+)', websiteText.text)))
	# Удаляем все невалидные ссылки
	for someurl in urls:
		if not someurl.startswith('/'):
			if not someurl.startswith('http://') and not someurl.endswith('.html'):
				#print("Delete", someurl)
				urls.remove(someurl)
	WritePageLinks(urls)
	# Удаляем все ссылки на другие сайты после их показа
	for someurl in urls:
		if not someurl.startswith('http://www.mosigra'):
			urls.remove(someurl)
	# Получить все почтовые адреса
	reobj = re.compile(r"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,6}\b", re.IGNORECASE)
	emails = list(set(re.findall(reobj, websiteText.text)))
	WritePageEmails(emails)
	AddNewMails(emails)
	global count
	# Проходим по всем доступным адресам
	for someurl in urls:
		if count <= 10:
			count += 1
			if not someurl.startswith('h') and someurl not in checkedPages:
				checkedPages.append(someurl)
				GetPageInfo(mainUrl + someurl)

allMails = []
checkedPages = []
count = 0
mainUrl = "http://mosigra.ru"
GetPageInfo(mainUrl)
print("All emails")
for email in allMails:
	print(email)