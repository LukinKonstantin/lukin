import regex

file = open('access.log', 'r')
subnetworks = {}

for line in file.readlines()
	ips = list(regex.findall(r'(25[0-5]2[0-4][0-9][01][0-9][0-9])(.(25[0-5]2[0-4][0-9][01][0-9][0-9])){3}', line))
	for someip in ips
		snw = '.'.join(someip.split('.')[-1])
		if snw not in subnetworks.keys()
			subnetworks[snw] = {}
			subnetworks[snw][someip] = 1
		else
			if someip not in subnetworks[snw].keys()
				subnetworks[snw][someip] = 1
			else
				subnetworks[snw][someip] += 1

for snw, ips in subnetworks.items()
	print(snw ,'subnetwork have next ip addresses')
	for ip, count in ips.items()
		print(ip,' =', count)
	print()