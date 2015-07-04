#!/usr/bin/python

#
# CSE/IT 353: Data and Computer Communications
# Project 2: Bridged Token Ring Emulation
# GenerateInput.py: Input file generator
#
# Written by: Juston Moore (previous TA)
# Dcember 2, 2010
#
# This script creates all of the input-files and config files
# needed as input to a project 2 implementation.
#
# NOTE: This program is designed to work with Python 3.1.1
#       It is incompatible with earlier versions.
#

import sys
import random

# Check for command-line argument
if len(sys.argv) != 2:
	print ("Usage: GenerateInput.py [Number of Nodes in System (Both Rings)]")
	# sys.exit()

#nodes = int(sys.argv[1])
nodes = 200

# Make sure that the specified number of nodes is correct
if nodes < 4 or nodes > 254:
	print ("You must have at least 4 nodes and no more than 254 nodes in both your rings.")
	sys.exit()
	
config1 = open("ring1.conf", "w")
config2 = open("ring2.conf", "w")

# Counters to ensure that there are at least 2 nodes in each ring
r1count = 0
r2count = 0

# Create entries for each node
for node in range(1, nodes+1):
	file = open("input-file-" + str(node), 'w')
	
	# Generate a random number of entries for each node, from 1 to 1000
	entries = random.randint(1, 1000)
	for entry in range(0, entries):
		# Randomly generate the destination node NOT the same as the source
		dest = node
		while dest == node:
			dest = random.randint(1, nodes)

		# Randomly generate a PDU size up to 254
		size = random.randint(1, 254)

		# Write file entry
		file.write(str(dest) + ',' + str(size) + ',')
		for data in range(0, size):
			# Write random PDU data (Uppercase ASCII)
			char = chr(random.randint(65,90))
			file.write(char)
		file.write("\n")
	
	# Randomly add to ring 1 or 2
	ring = random.randint(1,2)
	
	# Randomly assign unless load balancing is needed to obtain the 2 node / ring
	# minimum
	if r1count >= 2 and r2count < 2:
		config2.write(str(node) + "\n")
		r2count = r2count + 1
	elif r2count >= 2 and r1count < 2:
		config1.write(str(node) + "\n")
		r1count = r1count + 1
	elif ring == 1:
		config1.write(str(node) + "\n")
		r1count = r1count + 1
	else:
		config2.write(str(node) + "\n")
		r2count = r2count + 1

config1.close()
config2.close()