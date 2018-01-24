package main

import (
	"fmt"
)

type token struct {
	emitter int
	receiver int
	message string
}

func worker(in, out chan token, id int) {
	resToken := <-in
	fmt.Println("Channel - ", id)
	close(in)
	if resToken.message == "DIE"{
		out <- resToken 
		return
	}
	if id == resToken.receiver {
			fmt.Println("I'm the receiver! The message i've got:", resToken.message)
			resToken.message = "DIE"
			if resToken.emitter-resToken.receiver > 1{
				out <- resToken 
			}
			return
	}else if resToken.emitter == id{
		fmt.Println("Reciever not found!")
		return
	} else{
		out <- resToken
	}
	
}

func main() {
	const chanQuan int = 10
	emitter := 2
	receiver:= 2
	message := "Some data"
	var chans [chanQuan]chan token 
	for i := range chans {
		chans[i] = make(chan token)
	}

	for i := 0; i < chanQuan-1; i++ {
		go worker(chans[i], chans[i+1], i)

	}
	go worker(chans[chanQuan-1], chans[0], 9)
	chans[emitter+1] <- token{emitter, receiver, message}
	<-chans[emitter]
}